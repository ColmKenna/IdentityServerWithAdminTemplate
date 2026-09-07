using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Options;
using IdentityServerProject.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

/// <summary>
///     Proves the behaviour Work Item 8 exists for: a fresh clone reaches a running host with a
///     single <c>dotnet run --project AppHost</c>, with no manual migration-bundle step, because
///     <c>DevelopmentSeeder.ApplyDevelopmentMigrationsAsync</c> runs before
///     <see cref="IDatabaseSchemaReadinessValidator" /> would otherwise fail closed on the same
///     empty schema. SQLite-backed fixtures elsewhere in this suite bypass real migrations
///     entirely (<c>EnsureCreated</c>), so this is the one place that gap is actually closed —
///     against real SQL Server, using the same migrations production ships.
/// </summary>
[Collection(Task02SqlServerCollection.Name)]
public sealed class DevelopmentMigrationSqlServerTests
{
    [Fact]
    public async Task ApplyDevelopmentMigrationsAsync_AgainstFreshDatabases_CreatesAllThreeSchemasAndIsIdempotent()
    {
        await using FreshDatabases databases = FreshDatabases.Create();

        await using (DatabaseContexts contexts = databases.OpenContexts())
            await DevelopmentSeeder.ApplyDevelopmentMigrationsAsync(
                DevelopmentEnvironment.Instance, contexts.Identity, contexts.Configuration, contexts.Operational);

        await using (DatabaseContexts contexts = databases.OpenContexts())
        {
            Assert.Empty(await contexts.Identity.Database.GetPendingMigrationsAsync());
            Assert.Empty(await contexts.Configuration.Database.GetPendingMigrationsAsync());
            Assert.Empty(await contexts.Operational.Database.GetPendingMigrationsAsync());
        }

        // Re-running against an already-current database must not throw — a fresh clone that
        // restarts AppHost a second time needs this to be a no-op, not a failure.
        await using (DatabaseContexts contexts = databases.OpenContexts())
            await DevelopmentSeeder.ApplyDevelopmentMigrationsAsync(
                DevelopmentEnvironment.Instance, contexts.Identity, contexts.Configuration, contexts.Operational);
    }

    [Fact]
    public async Task FreshDevelopmentHost_MigratesBeforeTheReadinessCheckWouldOtherwiseBlockIt()
    {
        await using FreshDatabases databases = FreshDatabases.Create();

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("ConnectionStrings:IdentityDb", databases.IdentityConnectionString);
            builder.UseSetting("ConnectionStrings:IdentityConfigDb", databases.ConfigurationConnectionString);
            builder.UseSetting("ConnectionStrings:IdentityOperationalDb", databases.OperationalConnectionString);
            builder.UseSetting("Clients:RazorClientUri", "https://localhost:5001");
            builder.UseSetting("Clients:BlazorClientUri", "https://localhost:5002");
            builder.UseSetting("Clients:RazorSecret", "dev-secret-razor");
            builder.UseSetting("Clients:BlazorSecret", "dev-secret-blazor");
            builder.UseSetting("Seed:SysAdminEmail", "fresh-host-admin@example.test");
            builder.UseSetting("Seed:SysAdminPassword", "Password123!");
            builder.UseSetting("Seed:TestUserPassword", "Password123!");
        });

        using HttpClient client = factory.CreateClient();

        // No pre-migration happened above — these three databases do not exist yet. If
        // ApplyDevelopmentMigrationsAsync ran after the readiness check instead of before it (the
        // ordering the remediation guide originally proposed), this request fails with
        // "has pending migrations" before this line is ever reached.
        HttpResponseMessage response = await client.GetAsync("/.well-known/openid-configuration");

        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected discovery endpoint to succeed on a freshly migrated host, got {(int)response.StatusCode}.");

        await using DatabaseContexts contexts = databases.OpenContexts();
        Assert.Empty(await contexts.Identity.Database.GetPendingMigrationsAsync());
        Assert.Empty(await contexts.Configuration.Database.GetPendingMigrationsAsync());
        Assert.Empty(await contexts.Operational.Database.GetPendingMigrationsAsync());
    }

    /// <summary>Reports <c>Development</c> without depending on Moq for a value type this simple.</summary>
    private sealed class DevelopmentEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public static readonly DevelopmentEnvironment Instance = new();

        public string EnvironmentName { get; set; } = Microsoft.Extensions.Hosting.Environments.Development;
        public string ApplicationName { get; set; } = nameof(DevelopmentMigrationSqlServerTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    /// <summary>
    ///     Three never-before-used database names on the shared <see cref="Task02SqlServerFactory" />
    ///     container. <c>Database.MigrateAsync()</c> creates the database itself when it does not
    ///     exist, so nothing here pre-creates schema — these stay genuinely empty until the code
    ///     under test touches them.
    /// </summary>
    private sealed class FreshDatabases : IAsyncDisposable
    {
        public required string IdentityConnectionString { get; init; }
        public required string ConfigurationConnectionString { get; init; }
        public required string OperationalConnectionString { get; init; }

        public static FreshDatabases Create()
        {
            string suffix = Guid.NewGuid().ToString("N");
            return new FreshDatabases
            {
                IdentityConnectionString =
                    Task02SqlServerFactory.BuildConnectionString($"DevMigration_Identity_{suffix}"),
                ConfigurationConnectionString =
                    Task02SqlServerFactory.BuildConnectionString($"DevMigration_Configuration_{suffix}"),
                OperationalConnectionString =
                    Task02SqlServerFactory.BuildConnectionString($"DevMigration_Operational_{suffix}")
            };
        }

        public DatabaseContexts OpenContexts()
        {
            var identity = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlServer(IdentityConnectionString, sql => sql.MigrationsAssembly(
                        typeof(Program).Assembly.FullName))
                    .Options);

            ServiceProvider configurationProvider = StoreOptionsProvider(new ConfigurationStoreOptions());
            var configuration = new ConfigurationDbContext(
                new DbContextOptionsBuilder<ConfigurationDbContext>()
                    .UseApplicationServiceProvider(configurationProvider)
                    .UseSqlServer(ConfigurationConnectionString, sql => sql.MigrationsAssembly(
                        typeof(Program).Assembly.FullName))
                    .Options);

            ServiceProvider operationalProvider = StoreOptionsProvider(new OperationalStoreOptions());
            var operational = new PersistedGrantDbContext(
                new DbContextOptionsBuilder<PersistedGrantDbContext>()
                    .UseApplicationServiceProvider(operationalProvider)
                    .UseSqlServer(OperationalConnectionString, sql => sql.MigrationsAssembly(
                        typeof(Program).Assembly.FullName))
                    .Options);

            return new DatabaseContexts(identity, configuration, operational, configurationProvider,
                operationalProvider);
        }

        public async ValueTask DisposeAsync()
        {
            await DeleteDatabaseAsync(IdentityConnectionString);
            await DeleteDatabaseAsync(ConfigurationConnectionString);
            await DeleteDatabaseAsync(OperationalConnectionString);
        }

        private static async Task DeleteDatabaseAsync(string connectionString)
        {
            try
            {
                await using var connection = new Microsoft.Data.SqlClient.SqlConnection(
                    new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString)
                    {
                        InitialCatalog = "master"
                    }.ConnectionString);
                await connection.OpenAsync();

                string database = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString)
                    .InitialCatalog;
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"IF DB_ID('{database}') IS NOT NULL BEGIN " +
                    $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                    $"DROP DATABASE [{database}]; END";
                await command.ExecuteNonQueryAsync();
            }
            catch
            {
                // Best-effort cleanup. Names are GUID-suffixed, so a failure here cannot bleed
                // into another test — worst case is an orphaned database on a long-lived server.
            }
        }

        private static ServiceProvider StoreOptionsProvider<TOptions>(TOptions options) where TOptions : class
        {
            var services = new ServiceCollection();
            services.AddSingleton(options);
            return services.BuildServiceProvider();
        }
    }

    private sealed class DatabaseContexts(
        ApplicationDbContext identity,
        ConfigurationDbContext configuration,
        PersistedGrantDbContext operational,
        ServiceProvider configurationProvider,
        ServiceProvider operationalProvider) : IAsyncDisposable
    {
        public ApplicationDbContext Identity { get; } = identity;
        public ConfigurationDbContext Configuration { get; } = configuration;
        public PersistedGrantDbContext Operational { get; } = operational;

        public async ValueTask DisposeAsync()
        {
            await Identity.DisposeAsync();
            await Configuration.DisposeAsync();
            await Operational.DisposeAsync();
            await configurationProvider.DisposeAsync();
            await operationalProvider.DisposeAsync();
        }
    }
}
