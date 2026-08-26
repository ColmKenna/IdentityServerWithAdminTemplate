using IdentityServerProject;
using IdentityServerProject.Data;
using IdentityServerProject.Data.Adapters;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Diagnostics;
using IdentityServerProject.Services.SecretReveals;
using IdentityServerProject.Services.Users;
using IdentityServerProject.Services.Validation;
using IdentityServerProject.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddSqlServerDbContext<ApplicationDbContext>("IdentityDb");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();

var dataProtection = builder.Services
    .AddDataProtection()
    .SetApplicationName("IdentityServerProject")
    .PersistKeysToDbContext<ApplicationDbContext>();

if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
    var certificatePath = builder.Configuration["DataProtection:CertificatePath"];
    var certificatePassword = builder.Configuration["DataProtection:CertificatePassword"];
    if (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(certificatePassword))
    {
        throw new InvalidOperationException(
            "DataProtection:CertificatePath and DataProtection:CertificatePassword are required outside Development.");
    }

    var resolvedCertificatePath = Path.IsPathRooted(certificatePath)
        ? certificatePath
        : Path.Combine(builder.Environment.ContentRootPath, certificatePath);
    try
    {
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            resolvedCertificatePath,
            certificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);
        dataProtection.ProtectKeysWithCertificate(certificate);
    }
    catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
    {
        throw new InvalidOperationException(
            "The configured Data Protection certificate could not be loaded.", ex);
    }
}

builder.Services.AddOptions<AdminConsoleOptions>()
    .Bind(builder.Configuration.GetSection("AdminConsole"))
    .Validate(
        options => options.DefaultPageSize is >= AdminConsoleOptions.MinPageSize and <= AdminConsoleOptions.MaxPageSize,
        $"AdminConsole:DefaultPageSize must be between {AdminConsoleOptions.MinPageSize} and {AdminConsoleOptions.MaxPageSize}.")
    .ValidateOnStart();

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Admin mutations rotate the Identity security stamp. Validate it on every
// authenticated request so a revoked cookie cannot remain valid until the
// framework's default validation interval elapses.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.Zero);

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

var razorClientUri = AbsoluteHttpUri.Create(builder.Configuration["Clients:RazorClientUri"]
    ?? "https://localhost"); // Fallback for design-time tools
var blazorClientUri = AbsoluteHttpUri.Create(builder.Configuration["Clients:BlazorClientUri"]
    ?? "https://localhost"); // Fallback for design-time tools

var isBuilder = builder.Services
    .AddIdentityServer(options =>
    {
        options.EmitStaticAudienceClaim = true;
        options.Events.RaiseSuccessEvents = true;
        options.Events.RaiseFailureEvents = true;
        options.Events.RaiseInformationEvents = true;
    })
    .AddAspNetIdentity<ApplicationUser>()
    .AddConfigurationStore(options =>
    {
        options.ConfigureDbContext = db => db.UseSqlServer(
            builder.Configuration.GetConnectionString("IdentityConfigDb") ?? "Server=(localdb)\\mssqllocaldb;Database=IdentityConfigDb_DesignTime;Trusted_Connection=True;MultipleActiveResultSets=true",
            sql => sql
                .MigrationsAssembly(typeof(Program).Assembly.FullName)
                .EnableRetryOnFailure());
    })
    .AddOperationalStore(options =>
    {
        options.ConfigureDbContext = db => db.UseSqlServer(
            builder.Configuration.GetConnectionString("IdentityOperationalDb") ?? "Server=(localdb)\\mssqllocaldb;Database=IdentityOperationalDb_DesignTime;Trusted_Connection=True;MultipleActiveResultSets=true",
            sql => sql
                .MigrationsAssembly(typeof(Program).Assembly.FullName)
                .EnableRetryOnFailure());
    });

if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
{
    isBuilder.AddDeveloperSigningCredential(persistKey: false);
}
else
{
    var certPath = builder.Configuration["IdentityServer:SigningCertificatePath"];
    var certPassword = builder.Configuration["IdentityServer:SigningCertificatePassword"];
    if (!string.IsNullOrWhiteSpace(certPath) && !string.IsNullOrWhiteSpace(certPassword))
    {
        var resolvedCertPath = Path.IsPathRooted(certPath)
            ? certPath
            : Path.Combine(builder.Environment.ContentRootPath, certPath);
        var cert = X509CertificateLoader.LoadPkcs12FromFile(resolvedCertPath, certPassword, X509KeyStorageFlags.EphemeralKeySet);
        isBuilder.AddSigningCredential(cert);
    }
    else
    {
        isBuilder.AddDeveloperSigningCredential(persistKey: true);
    }
}

builder.Services.AddHealthChecks()
    .AddDbContextCheck<Duende.IdentityServer.EntityFramework.DbContexts.ConfigurationDbContext>("ConfigurationDb")
    .AddDbContextCheck<Duende.IdentityServer.EntityFramework.DbContexts.PersistedGrantDbContext>("OperationalDb");

// Required for dotnet ef CLI tools to instantiate DbContexts at design time
builder.Services.AddSingleton(new Duende.IdentityServer.EntityFramework.Options.ConfigurationStoreOptions());
builder.Services.AddSingleton(new Duende.IdentityServer.EntityFramework.Options.OperationalStoreOptions());

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SysAdminOnly", policy => policy.RequireRole(Config.SysAdminRole));
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", "SysAdminOnly");
});

builder.Services.AddIdentityServerAdminServices();

// Host-owned adapters for the persistence ports the admin-services library defines. The library
// stays free of any reference to ApplicationDbContext/ApplicationUser; these are the only classes
// that bridge the two.
builder.Services.AddScoped<IIdentityUserAdministrationStore, EfIdentityUserAdministrationStore>();
builder.Services.AddScoped<IdentityServerProject.Services.Roles.IRoleAdministrationStore, EfRoleAdministrationStore>();
builder.Services.AddScoped<IAdminAuditStore, EfAdminAuditStore>();
builder.Services.AddScoped<ISecretRevealStore, EfSecretRevealStore>();
builder.Services.AddScoped<IIdentityDiagnosticsStore, EfIdentityDiagnosticsStore>();

builder.Services.AddScoped<IDatabaseSchemaReadinessValidator, DatabaseSchemaReadinessValidator>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    // Deployment applies reviewed migration bundles. The web process only verifies
    // readiness and must do so before any environment-specific seed operation.
    await scope.ServiceProvider
        .GetRequiredService<IDatabaseSchemaReadinessValidator>()
        .EnsureReadyAsync();

    var sysAdminEmail = app.Configuration["Seed:SysAdminEmail"] ?? "admin@sales.local";
    var sysAdminPassword = app.Configuration["Seed:SysAdminPassword"] ?? "Password123!";

    await SeedData.SeedSysAdminAsync(
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
        scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(),
        sysAdminEmail,
        sysAdminPassword);
}

await DevelopmentSeeder.SeedIfDevelopmentAsync(app.Environment, async () =>
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;

    var identityDb = services.GetRequiredService<ApplicationDbContext>();
    var configDb = scope.ServiceProvider.GetRequiredService<Duende.IdentityServer.EntityFramework.DbContexts.ConfigurationDbContext>();
    var operationalDb = scope.ServiceProvider.GetRequiredService<Duende.IdentityServer.EntityFramework.DbContexts.PersistedGrantDbContext>();

    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

    var razorClientSecret = app.Configuration["Clients:RazorSecret"]
        ?? "DefaultRazorSecretForDevelopment";
    var blazorClientSecret = app.Configuration["Clients:BlazorSecret"]
        ?? "DefaultBlazorSecretForDevelopment";
    var sysAdminPassword = app.Configuration["Seed:SysAdminPassword"]
        ?? "Password123!";
    var sysAdminEmail = app.Configuration["Seed:SysAdminEmail"]
        ?? "admin@sales.local";
    var testUserPassword = app.Configuration["Seed:TestUserPassword"]
        ?? "Password123!";

    var seedClients = new List<SeedClientSpec>
    {
        new("razorclient", "Sales Razor Client", razorClientUri, razorClientSecret),
        new("blazorclient", "Sales Blazor Client", blazorClientUri, blazorClientSecret),
    };

    await SeedData.SeedAsync(
        identityDb,
        configDb,
        operationalDb,
        userManager,
        roleManager,
        seedClients,
        sysAdminEmail,
        sysAdminPassword,
        testUserPassword);
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseStatusCodePagesWithReExecute("/Error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseIdentityServer();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/Admin")).ExcludeFromDescription();

app.MapRazorPages();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
