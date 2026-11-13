using IdentityServerProject.Mobile.Core.Auth;
using IdentityServerProject.Mobile.Core.Domain;
using IdentityServerProject.Mobile.Core.Sync;
using IdentityServerProject.Mobile.Core.Sync.Contract;
using IdentityServerProject.Mobile.Core.Tests.Infrastructure;

namespace IdentityServerProject.Mobile.Core.Tests.Sync;

public sealed class SyncCoordinatorTests
{
    private static async Task SignInAsync(ClientHarness app, DateTimeOffset? expiresAt = null)
    {
        app.TokenStore.Stored = ClientHarness.NewTokens(expiresAt: expiresAt ?? app.Time.GetUtcNow().AddHours(1));
        await app.Session.InitializeAsync();
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        DateTime giveUp = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > giveUp) throw new TimeoutException("The expected background sync never happened.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task WhenSyncIsNotConfigured_ThePhoneIsPurelyLocal_AndNeverTalksToTheServer()
    {
        await using ClientHarness app = new(syncEnabled: false);
        await SignInAsync(app);
        await app.AddNoteAsync();

        await app.Coordinator.SyncNowAsync();

        Assert.Equal(SyncState.Disabled, app.Coordinator.Status.State);
        Assert.Equal(1, app.Coordinator.Status.PendingCount);
        Assert.Empty(app.Api.Pushes);
        Assert.Empty(app.Api.Pulls);
    }

    [Fact]
    public async Task WhenNobodyIsSignedIn_NothingIsSent()
    {
        await using ClientHarness app = new();

        await app.Coordinator.SyncNowAsync();

        Assert.Equal(SyncState.SignedOut, app.Coordinator.Status.State);
        Assert.Empty(app.Api.Pulls);
    }

    [Fact]
    public async Task WhenOffline_ChangesStayOnThePhone_AndTheStatusSaysSo()
    {
        await using ClientHarness app = new();
        await SignInAsync(app);
        await app.AddNoteAsync();
        await app.AddNoteAsync("Second");
        app.Network.IsOnline = false;

        await app.Coordinator.SyncNowAsync();

        Assert.Equal(SyncState.Offline, app.Coordinator.Status.State);
        Assert.Equal(2, app.Coordinator.Status.PendingCount);
        Assert.Empty(app.Api.Pushes);
    }

    [Fact]
    public async Task WhenTheSessionNeedsANewSignIn_NothingIsSent_AndTheUserIsToldToSignIn()
    {
        await using ClientHarness app = new();
        await SignInAsync(app, expiresAt: app.Time.GetUtcNow().AddSeconds(-5));
        app.Oidc.OnRefresh = _ => Task.FromResult(AuthResult.Failed(AuthFailure.Rejected));
        await app.Session.GetAccessTokenAsync();

        await app.Coordinator.SyncNowAsync();

        Assert.Equal(SyncState.AuthRequired, app.Coordinator.Status.State);
        Assert.Empty(app.Api.Pushes);
    }

    [Fact]
    public async Task ASuccessfulRun_EndsIdle_WithTheTimeItFinished()
    {
        await using ClientHarness app = new();
        await SignInAsync(app);
        await app.AddNoteAsync();
        app.Api.OnPush = request => new PushResponse(
            request.Changes.Select(c => new PushResult(c.Kind, c.Id, PushOutcome.Accepted, "s1", null, null)).ToList());

        await app.Coordinator.SyncNowAsync();

        SyncStatusSnapshot status = app.Coordinator.Status;
        Assert.Equal(SyncState.Idle, status.State);
        Assert.Equal(0, status.PendingCount);
        Assert.Equal(app.Time.GetUtcNow(), status.LastSyncedAt);
    }

    [Fact]
    public async Task WhenTheServerCannotBeReached_TheRunFailsGently_AndTheChangesAreStillPending()
    {
        await using ClientHarness app = new();
        await SignInAsync(app);
        await app.AddNoteAsync();
        app.Api.OnPush = _ => throw new HttpRequestException("timeout");

        await app.Coordinator.SyncNowAsync();

        Assert.Equal(SyncState.Failed, app.Coordinator.Status.State);
        Assert.Equal(1, app.Coordinator.Status.PendingCount);
        Assert.Contains("safe on this device", app.Coordinator.Status.Message);
    }

    [Fact]
    public async Task WhenTheServerRejectsTheToken_ItAsksForAFreshOne_AndSaysSignInIfTheIdentityServerAgrees()
    {
        await using ClientHarness app = new();
        await SignInAsync(app);
        await app.AddNoteAsync();
        app.Api.OnPush = _ => throw new SyncAuthenticationException("401");
        app.Oidc.OnRefresh = _ => Task.FromResult(AuthResult.Failed(AuthFailure.Rejected));

        await app.Coordinator.SyncNowAsync();

        Assert.Equal(1, app.Oidc.RefreshCalls);
        Assert.Equal(SyncState.AuthRequired, app.Coordinator.Status.State);
    }

    [Fact]
    public async Task WhenTheServerRejectsTheToken_ButTheRefreshWorks_ItIsJustARetryableFailure()
    {
        await using ClientHarness app = new();
        await SignInAsync(app);
        await app.AddNoteAsync();
        app.Api.OnPush = _ => throw new SyncAuthenticationException("401");
        app.Oidc.OnRefresh = current => Task.FromResult(AuthResult.Success(
            current with { AccessToken = "access-2", AccessTokenExpiresAt = app.Time.GetUtcNow().AddHours(1) }));

        await app.Coordinator.SyncNowAsync();

        Assert.Equal(SyncState.Failed, app.Coordinator.Status.State);
        Assert.Equal(SessionState.SignedIn, app.Session.State);
    }

    [Fact]
    public async Task RefusedChangesAreCountedSeparately_SoTheUserCanBeToldAboutThem()
    {
        await using ClientHarness app = new();
        await SignInAsync(app);
        Note note = await app.AddNoteAsync();
        app.Api.OnPush = _ => new PushResponse(
            [new PushResult(SyncEntityKind.Note, note.Id, PushOutcome.Rejected, null, null, "No")]);

        await app.Coordinator.SyncNowAsync();

        Assert.Equal(0, app.Coordinator.Status.PendingCount);
        Assert.Equal(1, app.Coordinator.Status.RejectedCount);
    }

    [Fact]
    public async Task ARequestWhileARunIsInProgress_IsNotLost_ItTriggersAnotherPass()
    {
        await using ClientHarness app = new();
        await SignInAsync(app);
        await app.AddNoteAsync("First");
        TaskCompletionSource firstPushStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirstPush = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int pushes = 0;
        app.Api.OnPush = request =>
        {
            if (Interlocked.Increment(ref pushes) == 1)
            {
                firstPushStarted.SetResult();
                releaseFirstPush.Task.GetAwaiter().GetResult();
            }

            return new PushResponse(
                request.Changes.Select(c => new PushResult(c.Kind, c.Id, PushOutcome.Accepted, "s", null, null)).ToList());
        };

        Task running = Task.Run(() => app.Coordinator.SyncNowAsync());
        await firstPushStarted.Task;
        await app.AddNoteAsync("Added during the run");
        await app.Coordinator.SyncNowAsync(); // returns at once; must make the running one go round again
        releaseFirstPush.SetResult();
        await running;

        Assert.Equal(2, app.Api.Pushes.Count);
        Assert.Equal(0, app.Coordinator.Status.PendingCount);
    }

    [Fact]
    public async Task RegainingAConnection_SyncsWhatWasQueuedOffline()
    {
        await using ClientHarness app = new();
        await SignInAsync(app);
        app.Network.IsOnline = false;
        await app.AddNoteAsync();
        app.Coordinator.Start();
        await EventuallyAsync(() => app.Coordinator.Status.State == SyncState.Offline);
        app.Api.OnPush = request => new PushResponse(
            request.Changes.Select(c => new PushResult(c.Kind, c.Id, PushOutcome.Accepted, "s", null, null)).ToList());

        app.Network.IsOnline = true;

        await EventuallyAsync(() => app.Api.Pushes.Count == 1 && app.Coordinator.Status.State == SyncState.Idle);
        Assert.Equal(0, app.Coordinator.Status.PendingCount);
    }

    [Fact]
    public async Task ALocalEdit_IsSyncedWithoutTheUserAskingForIt()
    {
        await using ClientHarness app = new();
        await SignInAsync(app);
        app.Coordinator.Start();
        await EventuallyAsync(() => app.Coordinator.Status.State == SyncState.Idle);
        app.Api.OnPush = request => new PushResponse(
            request.Changes.Select(c => new PushResult(c.Kind, c.Id, PushOutcome.Accepted, "s", null, null)).ToList());

        await app.AddNoteAsync("Typed on the train");

        await EventuallyAsync(() => app.Api.Pushes.Count == 1);
    }

    [Fact]
    public async Task StatusChanges_AreAnnounced()
    {
        await using ClientHarness app = new();
        await SignInAsync(app);
        List<SyncState> states = [];
        app.Coordinator.StatusChanged += () => states.Add(app.Coordinator.Status.State);

        await app.Coordinator.SyncNowAsync();

        Assert.Equal([SyncState.Syncing, SyncState.Idle], states);
    }
}
