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
using Microsoft.AspNetCore.Authorization;
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
        X509Certificate2 certificate = Pkcs12CertificateLoader.LoadFromFile(
            resolvedCertificatePath,
            certificatePassword);
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

    // Stated rather than inherited. HttpOnly and Lax are already the framework defaults;
    // writing them down is the point, because a consumer reading this file should see the
    // decisions rather than have to know what ASP.NET Core picks when nobody chooses.
    options.Cookie.HttpOnly = true;

    // SameSite is deliberately not set here. IdentityServer's ASP.NET Identity integration
    // post-configures this cookie to SameSiteMode.None, after this callback runs, so that it
    // survives the cross-site contexts the protocol needs — front-channel logout iframes and
    // the check-session endpoint. Setting it here would be overwritten and would leave this
    // file claiming a value the application does not use.
    //
    // That makes Secure non-negotiable rather than merely advisable: browsers reject
    // SameSite=None unless the cookie is also Secure, and the previous default of
    // SameAsRequest would have emitted exactly that combination over plain HTTP.
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

    // The framework defaults, made visible. A template cannot know a consumer's risk
    // appetite; it can make sure they are looking at a knob rather than an absence.
    // Revocation does not wait for these — SecurityStampValidator above runs every request.
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
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

        // Off by default, which means expired authorisation codes, refresh tokens and
        // reference tokens accumulate in PersistedGrants forever. The symptom arrives
        // months later as slow token operations, and the cause is a one-line opt-in.
        options.EnableTokenCleanup = true;
        options.TokenCleanupInterval = 3600; // seconds; stated rather than inherited

        // RemoveConsumedTokens is deliberately left off: it discards refresh tokens once
        // used, which also discards the evidence reuse detection relies on. That is a
        // consumer's call, not a default worth propagating.
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
    X509Certificate2 cert = Pkcs12CertificateLoader.LoadFromFile(resolvedCertPath, certPassword);
    isBuilder.AddSigningCredential(cert);
}

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ConfigurationDbContext>("ConfigurationDb")
    .AddDbContextCheck<PersistedGrantDbContext>("OperationalDb");

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SysAdminOnly", policy => policy.RequireRole(Config.SysAdminRole));

    // Protection by omission. An endpoint that declares no authorisation is denied rather
    // than served, so a page added outside the /Admin convention fails closed. Everything
    // meant to be reachable anonymously says so: the [AllowAnonymous] pages under /Account
    // and /Error, the root redirect below, and the health endpoints in ServiceDefaults.
    // Duende's protocol endpoints are unaffected — UseIdentityServer runs before
    // UseAuthorization and terminates those requests first.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
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

if (args.Contains("--bootstrap-admin"))
{
    await AdminBootstrapper.BootstrapSysAdminAsync(app.Services, app.Configuration);
    return;
}

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    // Deployment applies reviewed migration bundles. The web process only verifies
    // readiness and must do so before any environment-specific seed operation.
    await scope.ServiceProvider
        .GetRequiredService<IDatabaseSchemaReadinessValidator>()
        .EnsureReadyAsync();
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

    // All seed credentials are development-only values now that runtime admin bootstrapping
    // is decoupled into --bootstrap-admin. They are demanded when this seed runs, not on every start.
    string sysAdminEmail = app.Configuration.Required("Seed:SysAdminEmail");
    string sysAdminPassword = app.Configuration.Required("Seed:SysAdminPassword");
    string razorClientSecret = app.Configuration.Required("Clients:RazorSecret");
    string blazorClientSecret = app.Configuration.Required("Clients:BlazorSecret");
    string testUserPassword = app.Configuration.Required("Seed:TestUserPassword");

    var seedClients = new List<SeedClientSpec>
    {
        new("razorclient", "Example Razor Client", razorClientUri, razorClientSecret),
        new("blazorclient", "Example Blazor Client", blazorClientUri, blazorClientSecret)
    };

    // Duende's own Client defaults are these same three values, so leaving the section unset
    // in configuration changes nothing observable — it exists as a documented, effective knob
    // for a consumer who wants shorter-lived tokens, not as a mandatory setting.
    var tokenLifetimes = new TokenLifetimes(
        app.Configuration.GetValue(
            "IdentityServer:TokenLifetimes:AccessTokenLifetimeSeconds",
            TokenLifetimes.Default.AccessTokenLifetimeSeconds),
        app.Configuration.GetValue(
            "IdentityServer:TokenLifetimes:IdentityTokenLifetimeSeconds",
            TokenLifetimes.Default.IdentityTokenLifetimeSeconds),
        app.Configuration.GetValue(
            "IdentityServer:TokenLifetimes:AuthorizationCodeLifetimeSeconds",
            TokenLifetimes.Default.AuthorizationCodeLifetimeSeconds));

    await SeedData.SeedAsync(
        identityDb,
        configDb,
        operationalDb,
        userManager,
        roleManager,
        seedClients,
        sysAdminEmail,
        sysAdminPassword,
        testUserPassword,
        tokenLifetimes);
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

// Anonymous on purpose: challenging here would hand the login page returnUrl=/ when the
// console is where the user is actually going. The challenge happens at /Admin instead.
app.MapGet("/", () => Results.Redirect("/Admin")).ExcludeFromDescription().AllowAnonymous();

app.MapRazorPages();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;