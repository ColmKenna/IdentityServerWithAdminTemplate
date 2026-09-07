using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Options;
using IdentityServerProject.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace IdentityServerProject.Admin.Tests.Data;

/// <summary>
///     <see cref="DevelopmentSeeder.ApplyDevelopmentMigrationsAsync" /> and
///     <see cref="DevelopmentSeeder.SeedIfDevelopmentAsync" /> both call real, effectful
///     operations behind an environment guard. A test that only exercises the Development path
///     would not tell you the guard itself works — the tests below use connection strings that
///     fail fast if actually dialed, so a caught exception on the Development path is direct
///     evidence the guarded operation really runs, and its absence outside Development is direct
///     evidence it really didn't. The SQL-Server-backed proof that a fresh, unmigrated database
///     reaches a running host lives in <c>DevelopmentMigrationSqlServerTests</c>.
/// </summary>
public class DevelopmentSeederTests
{
    // Nothing listens on TCP port 1 on loopback, so a real connection attempt fails immediately
    // (connection refused) rather than waiting out a timeout — deterministic and fast in CI.
    private const string UnreachableConnectionString =
        "Server=127.0.0.1,1;Database=nonexistent;User ID=sa;Password=x;" +
        "TrustServerCertificate=True;Connect Timeout=2";

    [Fact]
    public async Task SeedIfDevelopmentAsync_WhenNotDevelopment_DoesNotExecuteTheCallback()
    {
        IHostEnvironment environment = FakeEnvironment(Environments.Production);

        bool executed = false;
        await DevelopmentSeeder.SeedIfDevelopmentAsync(environment, () =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        Assert.False(executed);
    }

    [Fact]
    public async Task SeedIfDevelopmentAsync_WhenDevelopment_ExecutesTheCallback()
    {
        IHostEnvironment environment = FakeEnvironment(Environments.Development);

        bool executed = false;
        await DevelopmentSeeder.SeedIfDevelopmentAsync(environment, () =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        Assert.True(executed);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task ApplyDevelopmentMigrationsAsync_OutsideDevelopment_NeverTouchesAnyDatabase(
        string environmentName)
    {
        IHostEnvironment environment = FakeEnvironment(environmentName);
        using UnreachableContexts contexts = CreateUnreachableContexts();

        // Would throw a SqlException on connection refusal if the guard let this reach the
        // database; the point of the test is that it never gets the chance to.
        Exception? exception = await Record.ExceptionAsync(() =>
            DevelopmentSeeder.ApplyDevelopmentMigrationsAsync(
                environment, contexts.Identity, contexts.Configuration, contexts.Operational));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ApplyDevelopmentMigrationsAsync_WhenDevelopment_ActuallyAttemptsTheMigration()
    {
        // Positive control for the three tests above: proves the unreachable connection string
        // really would fail the call, so their "no exception" result is the guard working and
        // not the connection string being harmlessly ignored for some other reason.
        IHostEnvironment environment = FakeEnvironment(Environments.Development);
        using UnreachableContexts contexts = CreateUnreachableContexts();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            DevelopmentSeeder.ApplyDevelopmentMigrationsAsync(
                environment, contexts.Identity, contexts.Configuration, contexts.Operational));
    }

    private static IHostEnvironment FakeEnvironment(string environmentName)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(environmentName);
        return environment.Object;
    }

    private static UnreachableContexts CreateUnreachableContexts()
    {
        var identity = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(UnreachableConnectionString)
                .Options);

        ServiceProvider configurationProvider = StoreOptionsProvider(new ConfigurationStoreOptions());
        var configuration = new ConfigurationDbContext(
            new DbContextOptionsBuilder<ConfigurationDbContext>()
                .UseApplicationServiceProvider(configurationProvider)
                .UseSqlServer(UnreachableConnectionString)
                .Options);

        ServiceProvider operationalProvider = StoreOptionsProvider(new OperationalStoreOptions());
        var operational = new PersistedGrantDbContext(
            new DbContextOptionsBuilder<PersistedGrantDbContext>()
                .UseApplicationServiceProvider(operationalProvider)
                .UseSqlServer(UnreachableConnectionString)
                .Options);

        return new UnreachableContexts(identity, configuration, operational, configurationProvider,
            operationalProvider);
    }

    private static ServiceProvider StoreOptionsProvider<TOptions>(TOptions options) where TOptions : class
    {
        var services = new ServiceCollection();
        services.AddSingleton(options);
        return services.BuildServiceProvider();
    }

    private sealed class UnreachableContexts(
        ApplicationDbContext identity,
        ConfigurationDbContext configuration,
        PersistedGrantDbContext operational,
        ServiceProvider configurationProvider,
        ServiceProvider operationalProvider) : IDisposable
    {
        public ApplicationDbContext Identity { get; } = identity;
        public ConfigurationDbContext Configuration { get; } = configuration;
        public PersistedGrantDbContext Operational { get; } = operational;

        public void Dispose()
        {
            Identity.Dispose();
            Configuration.Dispose();
            Operational.Dispose();
            configurationProvider.Dispose();
            operationalProvider.Dispose();
        }
    }
}
