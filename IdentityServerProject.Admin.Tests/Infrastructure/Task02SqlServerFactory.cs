using System.Collections.Concurrent;
using System.Data.Common;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Options;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using IdentityServerProject.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Task02SqlServerCollection : ICollectionFixture<Task02SqlServerFactory>
{
    public const string Name = "TASK-02 SQL Server";
}

/// <summary>
///     Disposable, migration-backed SQL Server databases for isolation and retry tests. By default,
///     the fixture starts a dedicated Docker container; CI can provide a server through
///     TASK_SQLSERVER_CONNECTION_STRING_TEMPLATE instead. These tests never use SQLite or EF InMemory
///     for transaction assertions.
/// </summary>
public sealed class Task02SqlServerFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";
    private static string? _containerConnectionStringTemplate;
    private readonly string _configurationDatabase = $"Task02_Configuration_{Guid.NewGuid():N}";
    private readonly string _identityDatabase = $"Task02_Identity_{Guid.NewGuid():N}";
    private readonly string _operationalDatabase = $"Task02_Operational_{Guid.NewGuid():N}";
    private bool _databasesDeleted;
    private MsSqlContainer? _sqlContainer;

    public string IdentityConnectionString { get; private set; } = null!;
    public string ConfigurationConnectionString { get; private set; } = null!;
    public string OperationalConnectionString { get; private set; } = null!;
    public RecordingBackChannelLogoutService BackChannelLogout { get; } = new();
    public IdentityDbCommandCounter IdentityCommands { get; } = new();

    public async Task InitializeAsync()
    {
        await StartContainerWhenNoServerIsConfiguredAsync();
        IdentityConnectionString = BuildConnectionString(_identityDatabase);
        ConfigurationConnectionString = BuildConnectionString(_configurationDatabase);
        OperationalConnectionString = BuildConnectionString(_operationalDatabase);
        await CreateSchemaAsync();
    }

    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (_sqlContainer != null)
        {
            await _sqlContainer.DisposeAsync();
            _sqlContainer = null;
        }

        _containerConnectionStringTemplate = null;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:IdentityDb", IdentityConnectionString);
        builder.UseSetting("ConnectionStrings:IdentityConfigDb", ConfigurationConnectionString);
        builder.UseSetting("ConnectionStrings:IdentityOperationalDb", OperationalConnectionString);
        builder.UseSetting("Clients:RazorClientUri", "https://localhost:5001");
        builder.UseSetting("Clients:BlazorClientUri", "https://localhost:5002");
        builder.UseSetting("Seed:SysAdminEmail", "admin@sales.local");
        builder.UseSetting("Seed:SysAdminPassword", "Password123!");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            RemoveDbContext<ApplicationDbContext>(services);
            services.AddScoped(_ => new ApplicationDbContext(
                SqlOptions<ApplicationDbContext>(IdentityConnectionString)
                    .AddInterceptors(IdentityCommands)
                    .Options));
            services.RemoveAll<IBackChannelLogoutService>();
            services.AddSingleton<IBackChannelLogoutService>(BackChannelLogout);
        });
    }

    public HttpClient CreateHttpsClient(bool allowAutoRedirect = false) => CreateClient(
        new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
            BaseAddress = new Uri("https://localhost")
        });

    public async Task RunInScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        if (!_databasesDeleted)
        {
            DeleteDatabase<ApplicationDbContext>(IdentityConnectionString);
            DeleteDatabase<ConfigurationDbContext>(ConfigurationConnectionString, new ConfigurationStoreOptions());
            DeleteDatabase<PersistedGrantDbContext>(OperationalConnectionString, new OperationalStoreOptions());
            _databasesDeleted = true;
        }
    }

    private async Task StartContainerWhenNoServerIsConfiguredAsync()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TASK_SQLSERVER_CONNECTION_STRING_TEMPLATE")))
            return;

        _sqlContainer = new MsSqlBuilder(SqlServerImage).Build();
        await _sqlContainer.StartAsync();

        var builder = new SqlConnectionStringBuilder(_sqlContainer.GetConnectionString())
        {
            InitialCatalog = "{database}",
            TrustServerCertificate = true,
            MultipleActiveResultSets = true
        };
        _containerConnectionStringTemplate = builder.ConnectionString;
    }

    private async Task CreateSchemaAsync()
    {
        await using (var identityDb = new ApplicationDbContext(
                         SqlOptions<ApplicationDbContext>(IdentityConnectionString).Options))
            await identityDb.Database.MigrateAsync();

        await using (ServiceProvider provider = StoreOptionsProvider(new ConfigurationStoreOptions()))
        await using (var configurationDb = new ConfigurationDbContext(
                         SqlOptions<ConfigurationDbContext>(ConfigurationConnectionString, provider).Options))
            await configurationDb.Database.MigrateAsync();

        await using ServiceProvider operationalProvider = StoreOptionsProvider(new OperationalStoreOptions());
        await using var operationalDb = new PersistedGrantDbContext(
            SqlOptions<PersistedGrantDbContext>(OperationalConnectionString, operationalProvider).Options);
        await operationalDb.Database.MigrateAsync();
    }

    private static DbContextOptionsBuilder<TContext> SqlOptions<TContext>(
        string connectionString,
        IServiceProvider? applicationServices = null)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        if (applicationServices != null) builder.UseApplicationServiceProvider(applicationServices);

        builder.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(Program).Assembly.FullName).EnableRetryOnFailure());
        return builder;
    }

    private static ServiceProvider StoreOptionsProvider<TOptions>(TOptions options)
        where TOptions : class
    {
        var services = new ServiceCollection();
        services.AddSingleton(options);
        return services.BuildServiceProvider();
    }

    private static void RemoveDbContext<TContext>(IServiceCollection services)
        where TContext : DbContext
    {
        var descriptors = services.Where(descriptor =>
                descriptor.ServiceType == typeof(DbContextOptions<TContext>)
                || descriptor.ServiceType == typeof(TContext)
                || (descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.Name.StartsWith("IDbContextPool", StringComparison.Ordinal)
                    && descriptor.ServiceType.GenericTypeArguments[0] == typeof(TContext))
                || (descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IConfigureOptions<>)
                    && descriptor.ServiceType.GenericTypeArguments[0] == typeof(DbContextOptions<TContext>)))
            .ToList();

        foreach (ServiceDescriptor descriptor in descriptors) services.Remove(descriptor);
    }

    private static void DeleteDatabase<TContext>(string connectionString, object? storeOptions = null)
        where TContext : DbContext
    {
        try
        {
            ServiceProvider? provider = storeOptions switch
            {
                ConfigurationStoreOptions configuration => StoreOptionsProvider(configuration),
                OperationalStoreOptions operational => StoreOptionsProvider(operational),
                _ => null
            };

            using (provider)
            {
                DbContextOptions<TContext> options = SqlOptions<TContext>(connectionString, provider).Options;
                using var context = (TContext)Activator.CreateInstance(typeof(TContext), options)!;
                context.Database.EnsureDeleted();
            }
        }
        catch
        {
            // Test cleanup must not hide the test result. Names are unique, so a failed cleanup
            // cannot affect a later run and can be removed by the normal LocalDB maintenance job.
        }
    }

    public static string BuildConnectionString(string database)
    {
        string? configuredTemplate = Environment.GetEnvironmentVariable("TASK_SQLSERVER_CONNECTION_STRING_TEMPLATE")
                                     ?? _containerConnectionStringTemplate;
        if (!string.IsNullOrWhiteSpace(configuredTemplate))
        {
            if (!configuredTemplate.Contains("{database}", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "TASK_SQLSERVER_CONNECTION_STRING_TEMPLATE must contain the {database} placeholder.");

            return configuredTemplate.Replace("{database}", database, StringComparison.Ordinal);
        }

        throw new InvalidOperationException(
            "The SQL Server test fixture has not started. Run the test through xUnit or configure " +
            "TASK_SQLSERVER_CONNECTION_STRING_TEMPLATE.");
    }
}

public sealed class IdentityDbCommandCounter : DbCommandInterceptor
{
    private int _readCount;

    public int ReadCount => Volatile.Read(ref _readCount);

    public void Reset() => Interlocked.Exchange(ref _readCount, 0);

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

    private void CountRead(DbCommand command)
    {
        if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            Interlocked.Increment(ref _readCount);
    }
}

public sealed class RecordingBackChannelLogoutService : IBackChannelLogoutService
{
    public ConcurrentQueue<LogoutNotificationContext> Calls { get; } = new();
    public Func<LogoutNotificationContext, CancellationToken, Task>? OnSendAsync { get; set; }

    public Task SendLogoutNotificationsAsync(
        LogoutNotificationContext notificationContext,
        CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(notificationContext);
        return OnSendAsync?.Invoke(notificationContext, cancellationToken) ?? Task.CompletedTask;
    }

    public void Reset()
    {
        OnSendAsync = null;
        while (Calls.TryDequeue(out _))
        {
        }
    }
}