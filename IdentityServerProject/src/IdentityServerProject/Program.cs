using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Options;
using IdentityServerProject;
using IdentityServerProject.Configuration;
using IdentityServerProject.Data;
using IdentityServerProject.Data.Adapters;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Diagnostics;
using IdentityServerProject.Services.Roles;
using IdentityServerProject.Services.SecretReveals;
using IdentityServerProject.Services.Users;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddSqlServerDbContext<ApplicationDbContext>("IdentityDb");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();

IDataProtectionBuilder dataProtection = builder.Services
    .AddDataProtection()
    .SetApplicationName("IdentityServerProject")
    .PersistKeysToDbContext<ApplicationDbContext>();

if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
    string? certificatePath = builder.Configuration["DataProtection:CertificatePath"];
    string? certificatePassword = builder.Configuration["DataProtection:CertificatePassword"];
    if (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(certificatePassword))
        throw new InvalidOperationException(
            "DataProtection:CertificatePath and DataProtection:CertificatePassword are required outside Development.");

    string resolvedCertificatePath = Path.IsPathRooted(certificatePath)
        ? certificatePath
        : Path.Combine(builder.Environment.ContentRootPath, certificatePath);
    try
    {
        X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(
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

IIdentityServerBuilder isBuilder = builder.Services
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
            builder.Configuration.GetConnectionString("IdentityConfigDb") ??
            "Server=(localdb)\\mssqllocaldb;Database=IdentityConfigDb_DesignTime;Trusted_Connection=True;MultipleActiveResultSets=true",
            sql => sql
                .MigrationsAssembly(typeof(Program).Assembly.FullName)
                .EnableRetryOnFailure());
    })
    .AddOperationalStore(options =>
    {
        options.ConfigureDbContext = db => db.UseSqlServer(
            builder.Configuration.GetConnectionString("IdentityOperationalDb") ??
            "Server=(localdb)\\mssqllocaldb;Database=IdentityOperationalDb_DesignTime;Trusted_Connection=True;MultipleActiveResultSets=true",
            sql => sql
                .MigrationsAssembly(typeof(Program).Assembly.FullName)
                .EnableRetryOnFailure());
    });

if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
    isBuilder.AddDeveloperSigningCredential(false);
else
{
    string? certPath = builder.Configuration["IdentityServer:SigningCertificatePath"];
    string? certPassword = builder.Configuration["IdentityServer:SigningCertificatePassword"];
    if (string.IsNullOrWhiteSpace(certPath) || string.IsNullOrWhiteSpace(certPassword))
        throw new InvalidOperationException(
            "IdentityServer:SigningCertificatePath and IdentityServer:SigningCertificatePassword are required outside Development.");

    string resolvedCertPath = Path.IsPathRooted(certPath)
        ? certPath
        : Path.Combine(builder.Environment.ContentRootPath, certPath);
    X509Certificate2 cert =
        X509CertificateLoader.LoadPkcs12FromFile(resolvedCertPath, certPassword,
            X509KeyStorageFlags.EphemeralKeySet);
    isBuilder.AddSigningCredential(cert);
}

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ConfigurationDbContext>("ConfigurationDb")
    .AddDbContextCheck<PersistedGrantDbContext>("OperationalDb");

// Required for dotnet ef CLI tools to instantiate DbContexts at design time
builder.Services.AddSingleton(new ConfigurationStoreOptions());
builder.Services.AddSingleton(new OperationalStoreOptions());

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SysAdminOnly", policy => policy.RequireRole(Config.SysAdminRole));
});

builder.Services.AddRazorPages(options => { options.Conventions.AuthorizeFolder("/Admin", "SysAdminOnly"); });

builder.Services.AddIdentityServerAdminServices();

// Host-owned adapters for the persistence ports the admin-services library defines. The library
// stays free of any reference to ApplicationDbContext/ApplicationUser; these are the only classes
// that bridge the two.
builder.Services.AddScoped<IIdentityUserAdministrationStore, EfIdentityUserAdministrationStore>();
builder.Services.AddScoped<IRoleAdministrationStore, EfRoleAdministrationStore>();
builder.Services.AddScoped<IAdminAuditStore, EfAdminAuditStore>();
builder.Services.AddScoped<ISecretRevealStore, EfSecretRevealStore>();
builder.Services.AddScoped<IIdentityDiagnosticsStore, EfIdentityDiagnosticsStore>();

builder.Services.AddScoped<IDatabaseSchemaReadinessValidator, DatabaseSchemaReadinessValidator>();

WebApplication app = builder.Build();

// Read before the readiness check so a missing credential fails on its own terms rather
// than after a database round trip. This administrator is seeded in every environment, so
// the values can never fall back to a literal: see RequiredConfigurationExtensions.
string sysAdminEmail = app.Configuration.Required("Seed:SysAdminEmail");
string sysAdminPassword = app.Configuration.Required("Seed:SysAdminPassword");

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    // Deployment applies reviewed migration bundles. The web process only verifies
    // readiness and must do so before any environment-specific seed operation.
    await scope.ServiceProvider
        .GetRequiredService<IDatabaseSchemaReadinessValidator>()
        .EnsureReadyAsync();

    await SeedData.SeedSysAdminAsync(
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
        scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(),
        sysAdminEmail,
        sysAdminPassword);
}

await DevelopmentSeeder.SeedIfDevelopmentAsync(app.Environment, async () =>
{
    using IServiceScope scope = app.Services.CreateScope();
    IServiceProvider services = scope.ServiceProvider;

    ApplicationDbContext identityDb = services.GetRequiredService<ApplicationDbContext>();
    ConfigurationDbContext configDb = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
    PersistedGrantDbContext operationalDb = scope.ServiceProvider.GetRequiredService<PersistedGrantDbContext>();

    UserManager<ApplicationUser> userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    RoleManager<IdentityRole> roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

    // The administrator pair is already resolved above; only the development-only values
    // are read here, so they are demanded when this seed runs rather than on every start.
    string razorClientSecret = app.Configuration.Required("Clients:RazorSecret");
    string blazorClientSecret = app.Configuration.Required("Clients:BlazorSecret");
    string testUserPassword = app.Configuration.Required("Seed:TestUserPassword");

    var seedClients = new List<SeedClientSpec>
    {
        new("razorclient", "Sales Razor Client", razorClientUri, razorClientSecret),
        new("blazorclient", "Sales Blazor Client", blazorClientUri, blazorClientSecret)
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