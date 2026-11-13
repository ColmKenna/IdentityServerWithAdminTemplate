using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IdentityServerProject.Mobile.Core.Auth;
using IdentityServerProject.Mobile.Core.Data;

namespace IdentityServerProject.Mobile.Core.Sync;

public enum SyncState
{
    /// <summary>Sync is not configured; the app is purely local.</summary>
    Disabled,

    SignedOut,

    /// <summary>No connection. Edits are being kept on the device.</summary>
    Offline,

    Idle,
    Syncing,

    /// <summary>The user has to sign in again before anything can be sent.</summary>
    AuthRequired,

    /// <summary>The last attempt failed. It will be retried.</summary>
    Failed
}

public sealed record SyncStatusSnapshot(
    SyncState State,
    int PendingCount,
    int RejectedCount,
    DateTimeOffset? LastSyncedAt,
    string? Message,
    int ConflictsInLastRun)
{
    public static SyncStatusSnapshot Initial { get; } = new(SyncState.Disabled, 0, 0, null, null, 0);
}

/// <summary>
///     Decides when to run <see cref="SyncEngine" /> and tells the UI how it went: at start-up, when the
///     connection returns, shortly after a local edit, on a timer, and on demand. Never throws to its
///     triggers; failures become <see cref="SyncState.Failed" /> and the next trigger retries.
/// </summary>
public sealed class SyncCoordinator(
    SyncEngine engine,
    LocalDatabase database,
    ISessionService session,
    INetworkStatus network,
    LocalDataChanges dataChanges,
    IOptions<MobileClientOptions> options,
    TimeProvider time,
    ILogger<SyncCoordinator> logger) : IDisposable
{
    private readonly SemaphoreSlim runGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private int started;
    private int deferredScheduled;
    private int disposed;
    private volatile bool rerunRequested;

    public SyncStatusSnapshot Status { get; private set; } = SyncStatusSnapshot.Initial;

    /// <summary>Raised whenever <see cref="Status" /> changes. May fire on any thread.</summary>
    public event Action? StatusChanged;

    public void Start()
    {
        if (Interlocked.Exchange(ref started, 1) == 1) return;

        network.Changed += OnNetworkChanged;
        dataChanges.Changed += OnDataChanged;
        session.Changed += OnSessionChanged;

        _ = RunLoopAsync(lifetime.Token);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1) return;

        network.Changed -= OnNetworkChanged;
        dataChanges.Changed -= OnDataChanged;
        session.Changed -= OnSessionChanged;
        lifetime.Cancel();
        lifetime.Dispose();
    }

    /// <summary>Runs a sync now. If one is already running it is asked to go round again, so this request is not lost.</summary>
    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (!await runGate.WaitAsync(0, cancellationToken))
        {
            rerunRequested = true;
            return;
        }

        try
        {
            do
            {
                rerunRequested = false;
                await RunOnceAsync(cancellationToken);
            } while (rerunRequested && !cancellationToken.IsCancellationRequested);
        }
        finally
        {
            runGate.Release();
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (BlockedState() is { } blocked)
        {
            await PublishAsync(blocked, null, 0, cancellationToken);
            return;
        }

        await PublishAsync(SyncState.Syncing, null, 0, cancellationToken);

        try
        {
            SyncRunResult result = await engine.RunAsync(cancellationToken);
            await database.SetMetadataAsync(
                LocalDatabase.LastSyncedAtKey, time.GetUtcNow().UtcTicks.ToString(), cancellationToken);
            await PublishAsync(SyncState.Idle, null, result.Conflicts, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down; leave the last published state alone.
        }
        catch (SyncAuthenticationException)
        {
            // The token was refused. Ask for a fresh one: if the identity server refuses that too the
            // session flips to ReauthRequired, otherwise the next run simply works.
            await session.GetAccessTokenAsync(forceRefresh: true, cancellationToken);
            await PublishAsync(
                session.State == SessionState.ReauthRequired ? SyncState.AuthRequired : SyncState.Failed,
                "The server didn't accept this device's sign-in.", 0, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            logger.LogInformation(ex, "Sync could not reach the server.");
            await PublishAsync(SyncState.Failed, "Couldn't reach the server. Your changes are safe on this device.", 0, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sync failed.");
            await PublishAsync(SyncState.Failed, "Sync failed. Your changes are safe on this device.", 0, cancellationToken);
        }
    }

    private SyncState? BlockedState()
    {
        if (!options.Value.SyncEnabled) return SyncState.Disabled;
        if (session.User is null) return SyncState.SignedOut;
        if (session.State == SessionState.ReauthRequired) return SyncState.AuthRequired;
        if (!network.IsOnline) return SyncState.Offline;
        return null;
    }

    private async Task PublishAsync(SyncState state, string? message, int conflicts, CancellationToken cancellationToken)
    {
        (int pending, int rejected) = await database.CountUnsyncedAsync(cancellationToken);
        string? lastTicks = await database.GetMetadataAsync(LocalDatabase.LastSyncedAtKey, cancellationToken);
        DateTimeOffset? lastSynced = long.TryParse(lastTicks, out long ticks) ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

        Status = new SyncStatusSnapshot(state, pending, rejected, lastSynced, message, conflicts);
        StatusChanged?.Invoke();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SyncNowAsync(cancellationToken);

            using PeriodicTimer timer = new(options.Value.SyncInterval, time);
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await SyncNowAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The sync loop stopped unexpectedly.");
        }
    }

    private void OnNetworkChanged(bool online) => Defer(TimeSpan.Zero);

    private void OnSessionChanged() => Defer(TimeSpan.Zero);

    private void OnDataChanged(ChangeOrigin origin)
    {
        // Both origins change the counts the UI shows, but only the user's own edits are a reason to
        // talk to the server; reacting to the sync's own writes would trigger sync after sync.
        if (origin == ChangeOrigin.Local) Defer(options.Value.SyncDebounce);
        else _ = RefreshStatusAsync(lifetime.Token);
    }

    /// <summary>Coalesces a burst of triggers into a single run after <paramref name="delay" />.</summary>
    private void Defer(TimeSpan delay)
    {
        if (Interlocked.Exchange(ref deferredScheduled, 1) == 1) return;

        _ = Task.Run(async () =>
        {
            try
            {
                if (delay > TimeSpan.Zero) await Task.Delay(delay, time, lifetime.Token);

                // Cleared before running so a trigger that arrives mid-run schedules the next one.
                Interlocked.Exchange(ref deferredScheduled, 0);
                await SyncNowAsync(lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // Disposed.
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref deferredScheduled, 0);
                logger.LogError(ex, "A deferred sync trigger failed.");
            }
        });
    }

    private async Task RefreshStatusAsync(CancellationToken cancellationToken)
    {
        // Keeps the badge honest after a pull. A run in progress publishes its own counts when it ends.
        if (runGate.CurrentCount == 0) return;

        try
        {
            await PublishAsync(Status.State, Status.Message, Status.ConflictsInLastRun, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Refreshing the sync status failed.");
        }
    }
}
