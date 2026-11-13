using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using IdentityServerProject.Mobile.Core.Auth;
using IdentityServerProject.Mobile.Core.Data;
using IdentityServerProject.Mobile.Core.Domain;
using IdentityServerProject.Mobile.Core.Services;
using IdentityServerProject.Mobile.Core.Sync;

namespace IdentityServerProject.Mobile.Core.Tests.Infrastructure;

/// <summary>
///     The real client composition (<c>AddMobileApp</c>) over a real SQLite file in a temp folder, with
///     only the edges replaced by fakes: the credential store, the identity server, the network, the
///     sync API and the clock. Tests therefore exercise real migrations and real queries.
/// </summary>
public sealed class ClientHarness : IAsyncDisposable
{
    // One root per test process, swept when the process exits. Each harness removes its own folder on dispose,
    // so this only ever finds what a test class forgot to dispose; without it that is silent, because a test that
    // leaks still passes and the folders just pile up in %TEMP%.
    private static readonly string RunDirectory = Path.Combine(Path.GetTempPath(), "mobile-app-tests", Guid.NewGuid().ToString("N"));

    static ClientHarness() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(RunDirectory)) Directory.Delete(RunDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort, like the per-harness cleanup.
            }
        };

    private readonly string directory = Path.Combine(RunDirectory, Guid.NewGuid().ToString("N"));

    public ClientHarness(bool syncEnabled = true, Action<IServiceCollection>? configureServices = null)
    {
        Directory.CreateDirectory(directory);

        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(Time);
        services.AddMobileApp(options =>
        {
            options.DatabasePath = Path.Combine(directory, "mobile-app.db3");
            options.SyncEnabled = syncEnabled;
            options.SyncDebounce = TimeSpan.Zero;
            options.SyncInterval = TimeSpan.FromHours(1);
            options.Authority = "https://idp.test";
        });

        // Replace the edges. The last registration of a service type is the one that is resolved.
        services.AddSingleton<ISecureTokenStore>(TokenStore);
        services.AddSingleton<IOidcGateway>(Oidc);
        services.AddSingleton<INetworkStatus>(Network);
        services.AddSingleton<ISyncApi>(Api);
        configureServices?.Invoke(services);

        Services = services.BuildServiceProvider();
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    public FakeTokenStore TokenStore { get; } = new();
    public FakeOidcGateway Oidc { get; } = new();
    public FakeNetworkStatus Network { get; } = new();
    public FakeSyncApi Api { get; } = new();
    public ServiceProvider Services { get; }

    public LocalDatabase Database => Services.GetRequiredService<LocalDatabase>();
    public INoteService Notes => Services.GetRequiredService<INoteService>();
    public ISessionService Session => Services.GetRequiredService<ISessionService>();
    public SyncEngine Engine => Services.GetRequiredService<SyncEngine>();
    public SyncCoordinator Coordinator => Services.GetRequiredService<SyncCoordinator>();
    public LocalDataChanges DataChanges => Services.GetRequiredService<LocalDataChanges>();

    public async Task<Note> AddNoteAsync(string title = "Shopping list", string? body = null) =>
        await Notes.SaveAsync(new Note { Title = title, Body = body });

    /// <summary>Marks every stored record as acknowledged by the server, as a completed sync would.</summary>
    public async Task MarkEverythingSyncedAsync(string version = "v1")
    {
        await using MobileDbContext context = await Database.OpenAsync();
        List<SyncEntity> all = [];
        all.AddRange(await context.Notes.IgnoreQueryFilters().ToListAsync());

        foreach (SyncEntity entity in all)
        {
            entity.SyncStatus = SyncStatus.Synced;
            entity.ServerVersion = version;
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Reads one record straight from the database, tombstones included.</summary>
    public async Task<T?> RawAsync<T>(Guid id) where T : SyncEntity
    {
        await using MobileDbContext context = await Database.OpenAsync();
        return await context.Set<T>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(e => e.Id == id);
    }

    public static TokenSet NewTokens(string subject = "user-1", DateTimeOffset? expiresAt = null, string? refreshToken = "refresh-1") =>
        new("access-1", refreshToken, "id-1", expiresAt ?? new DateTimeOffset(2027, 3, 1, 10, 0, 0, TimeSpan.Zero),
            new UserProfile(subject, "Dana Quinn", "dana@example.test", ["Member"]));

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();

        // Pooled connections keep the file open, which on Windows blocks deleting it. Only this harness's
        // pool: ClearAllPools is process-wide and closes connections that tests running in parallel are using.
        await using (SqliteConnection connection = new($"Data Source={Path.Combine(directory, "mobile-app.db3")}"))
            SqliteConnection.ClearPool(connection);
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is harmless; a failed cleanup must not fail the test.
        }
    }
}
