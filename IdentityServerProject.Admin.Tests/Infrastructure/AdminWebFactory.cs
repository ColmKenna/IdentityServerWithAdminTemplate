using System;
using Microsoft.AspNetCore.DataProtection;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using IdentityServerProject.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

public class AdminWebFactory : WebApplicationFactory<Program>
{
    private SqliteConnection _connection = default!;
    public ConfigurationDbCommandCounter ConfigurationCommands { get; } = new();
    public ConfigurationDbCommandCounter PersistedGrantCommands { get; } = new();

    static AdminWebFactory()
    {
        Environment.SetEnvironmentVariable("Clients__RazorClientUri", "https://localhost:5001");
        Environment.SetEnvironmentVariable("Clients__BlazorClientUri", "https://localhost:5002");
        Environment.SetEnvironmentVariable("Clients__RazorSecret", "secret");
        Environment.SetEnvironmentVariable("Clients__BlazorSecret", "secret");
        Environment.SetEnvironmentVariable("Seed__SysAdminPassword", "Password123!");
        Environment.SetEnvironmentVariable("Seed__SysAdminEmail", "admin@sales.local");
        Environment.SetEnvironmentVariable("Seed__TestUserPassword", "Password123!");
        Environment.SetEnvironmentVariable("ConnectionStrings__IdentityDb", "Server=localhost;Database=dummy;");
        Environment.SetEnvironmentVariable("ConnectionStrings__IdentityConfigDb", "Server=localhost;Database=dummy;");
        Environment.SetEnvironmentVariable("ConnectionStrings__IdentityOperationalDb", "Server=localhost;Database=dummy;");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureTestServices(services =>
        {
            // Remove existing health checks to avoid duplicates
            var healthCheckDescriptors = services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckRegistration)).ToList();
            foreach (var descriptor in healthCheckDescriptors)
            {
                services.Remove(descriptor);
            }
            services.Configure<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
            });


            // Override IdentityServer DbContexts directly on the singletons
            var configStoreOptions = services.FirstOrDefault(d => d.ServiceType == typeof(Duende.IdentityServer.EntityFramework.Options.ConfigurationStoreOptions));
            if (configStoreOptions?.ImplementationInstance is Duende.IdentityServer.EntityFramework.Options.ConfigurationStoreOptions configOptions)
            {
                configOptions.ConfigureDbContext = b => b
                    .UseSqlite(_connection)
                    .AddInterceptors(ConfigurationCommands);
            }

            var opStoreOptions = services.FirstOrDefault(d => d.ServiceType == typeof(Duende.IdentityServer.EntityFramework.Options.OperationalStoreOptions));
            if (opStoreOptions?.ImplementationInstance is Duende.IdentityServer.EntityFramework.Options.OperationalStoreOptions opOptions)
            {
                opOptions.ConfigureDbContext = b => b
                    .UseSqlite(_connection)
                    .AddInterceptors(PersistedGrantCommands);
            }

            // Remove ApplicationDbContext to re-register it
            RemoveDbContext<ApplicationDbContext>(services);

            // Re-register ApplicationDbContext by manually instantiating it, 
            // bypassing any hidden Aspire AddDbContext/Pool extensions that might cache SQL Server
            services.AddScoped<ApplicationDbContext>(sp =>
            {
                var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
                optionsBuilder.UseSqlite(_connection);
                return new ApplicationDbContext(optionsBuilder.Options);
            });

            // This harness creates a composite SQLite schema explicitly around host
            // construction. Production migration readiness is covered by real-SQL tests.
            services.AddScoped<IDatabaseSchemaReadinessValidator, TestingSchemaReadinessValidator>();

            // Isolate test Data Protection
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            // Add test authentication handler
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "TestScheme";
                options.DefaultChallengeScheme = "TestScheme";
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", options => { });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Program.cs unconditionally seeds the SysAdmin role/user (in every environment,
        // including "Testing") as part of its top-level startup code, which runs inside
        // base.CreateHost(builder) below (host.Start() executes Program's Main via the
        // WebApplicationFactory/DeferredHostBuilder mechanism). That means the ApplicationDbContext
        // schema must already exist on the shared SQLite connection *before* base.CreateHost is
        // called, or the seed step fails with "no such table: AspNetRoles". Create it directly
        // against the connection here, independent of the DI container that base.CreateHost builds.
        var bootstrapOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        using (var bootstrapDb = new ApplicationDbContext(bootstrapOptions))
        {
            bootstrapDb.Database.EnsureCreated();
        }

        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();

        // EnsureCreated() short-circuits when any table already exists. All contexts share
        // this test-only SQLite connection, so create the other two models explicitly.
        CreateTablesForSharedConnection(scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>());
        CreateTablesForSharedConnection(scope.ServiceProvider.GetRequiredService<PersistedGrantDbContext>());

        return host;
    }

    private static void CreateTablesForSharedConnection(DbContext context)
    {
        var creator = (Microsoft.EntityFrameworkCore.Storage.RelationalDatabaseCreator)
            context.Database.GetService<Microsoft.EntityFrameworkCore.Storage.IDatabaseCreator>();
        creator.CreateTables();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Dispose();
        }
    }

    private static void RemoveDbContext<TDbContext>(IServiceCollection services) where TDbContext : DbContext
    {
        var descriptors = services.Where(d => 
            d.ServiceType == typeof(DbContextOptions<TDbContext>) ||
            d.ServiceType == typeof(TDbContext) ||
            (d.ServiceType.IsGenericType && d.ServiceType.Name.StartsWith("IDbContextPool") && d.ServiceType.GenericTypeArguments[0] == typeof(TDbContext)) ||
            (d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Options.IConfigureOptions<>) && d.ServiceType.GenericTypeArguments[0] == typeof(DbContextOptions<TDbContext>))
        ).ToList();
        
        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }

    public async Task RunInScopeAsync(Func<IServiceProvider, Task> action)
    {
        using var scope = Services.CreateScope();
        
        await action(scope.ServiceProvider);
    }
}

public sealed class ConfigurationDbCommandCounter : DbCommandInterceptor
{
    private readonly object _gate = new();
    private readonly List<string> _commands = new();
    private int _readCount;

    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_gate)
            {
                return _commands.ToArray();
            }
        }
    }

    public int ReadCount => Volatile.Read(ref _readCount);

    public void Reset()
    {
        lock (_gate)
        {
            _commands.Clear();
        }

        Interlocked.Exchange(ref _readCount, 0);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        CountRead(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        CountRead(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    private void CountRead(DbCommand command)
    {
        Record(command);
        if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _readCount);
        }
    }

    private void Record(DbCommand command)
    {
        lock (_gate)
        {
            _commands.Add(command.CommandText);
        }
    }
}

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var mode = Request.Headers["X-Test-Auth"].ToString();
        if (string.Equals(mode, "anonymous", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "admin-test-id"),
            new Claim(ClaimTypes.Name, "admin@sales.local")
        };

        if (!string.Equals(mode, "non-admin", StringComparison.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, Config.SysAdminRole));
        }

        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var returnUrl = Uri.EscapeDataString($"{Request.PathBase}{Request.Path}{Request.QueryString}");
        Response.Redirect($"/Account/Login?returnUrl={returnUrl}");
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.Redirect("/Account/AccessDenied");
        return Task.CompletedTask;
    }
}

internal sealed class TestingSchemaReadinessValidator : IDatabaseSchemaReadinessValidator
{
    public Task EnsureReadyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
