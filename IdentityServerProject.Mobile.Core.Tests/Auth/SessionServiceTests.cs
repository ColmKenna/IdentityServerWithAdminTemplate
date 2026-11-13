using IdentityServerProject.Mobile.Core.Auth;
using IdentityServerProject.Mobile.Core.Data;
using IdentityServerProject.Mobile.Core.Domain;
using IdentityServerProject.Mobile.Core.Tests.Infrastructure;

namespace IdentityServerProject.Mobile.Core.Tests.Auth;

public sealed class SessionServiceTests : IAsyncLifetime
{
    private readonly ClientHarness app = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await app.DisposeAsync();

    [Fact]
    public async Task Initialize_WithStoredTokens_IsSignedInWithoutTouchingTheNetwork()
    {
        app.TokenStore.Stored = ClientHarness.NewTokens("user-1");

        await app.Session.InitializeAsync();

        Assert.Equal(SessionState.SignedIn, app.Session.State);
        Assert.Equal("Dana Quinn", app.Session.User!.Name);
        Assert.Equal(0, app.Oidc.RefreshCalls);
        Assert.Equal(0, app.Oidc.SignInCalls);
    }

    [Fact]
    public async Task Initialize_WithNothingStored_IsSignedOut()
    {
        await app.Session.InitializeAsync();

        Assert.Equal(SessionState.SignedOut, app.Session.State);
        Assert.Null(app.Session.User);
    }

    [Fact]
    public async Task SignIn_Success_StoresTheTokensAndRemembersWhoOwnsTheLocalData()
    {
        TokenSet issued = ClientHarness.NewTokens("user-1");
        app.Oidc.OnSignIn = () => AuthResult.Success(issued);

        AuthResult result = await app.Session.SignInAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(SessionState.SignedIn, app.Session.State);
        Assert.Equal(issued, app.TokenStore.Stored);
        Assert.Equal("user-1", await app.Database.GetMetadataAsync(LocalDatabase.OwnerSubjectKey));
    }

    [Theory]
    [InlineData(AuthFailure.Cancelled)]
    [InlineData(AuthFailure.Unreachable)]
    [InlineData(AuthFailure.Other)]
    public async Task SignIn_Failure_ChangesNothing(AuthFailure failure)
    {
        app.Oidc.OnSignIn = () => AuthResult.Failed(failure, "nope");

        AuthResult result = await app.Session.SignInAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(failure, result.Failure);
        Assert.Equal(SessionState.SignedOut, app.Session.State);
        Assert.Null(app.TokenStore.Stored);
    }

    [Fact]
    public async Task SignIn_AsTheSameAccountAgain_KeepsTheLocalData()
    {
        app.Oidc.OnSignIn = () => AuthResult.Success(ClientHarness.NewTokens("user-1"));
        await app.Session.SignInAsync();
        await app.AddNoteAsync("Mine");

        await app.Session.SignInAsync();

        Assert.Single(await app.Notes.SearchAsync(null));
    }

    [Fact]
    public async Task SignIn_AsADifferentAccount_RemovesTheFirstAccountsData()
    {
        app.Oidc.OnSignIn = () => AuthResult.Success(ClientHarness.NewTokens("user-1"));
        await app.Session.SignInAsync();
        await app.AddNoteAsync("First user's note");

        app.Oidc.OnSignIn = () => AuthResult.Success(ClientHarness.NewTokens("user-2"));
        await app.Session.SignInAsync();

        Assert.Empty(await app.Notes.SearchAsync(null));
        Assert.Equal("user-2", await app.Database.GetMetadataAsync(LocalDatabase.OwnerSubjectKey));
    }

    [Fact]
    public async Task SignOut_RemovesTheTokensAndAllLocalData()
    {
        app.Oidc.OnSignIn = () => AuthResult.Success(ClientHarness.NewTokens());
        await app.Session.SignInAsync();
        await app.AddNoteAsync();

        await app.Session.SignOutAsync();

        Assert.Equal(SessionState.SignedOut, app.Session.State);
        Assert.Null(app.Session.User);
        Assert.Null(app.TokenStore.Stored);
        Assert.Empty(await app.Notes.SearchAsync(null));
        Assert.Equal(1, app.Oidc.SignOutCalls);
    }

    [Fact]
    public async Task SignOut_StillSucceedsWhenTheServerCannotBeReached()
    {
        app.Oidc.OnSignIn = () => AuthResult.Success(ClientHarness.NewTokens());
        await app.Session.SignInAsync();
        app.Oidc.OnSignOut = _ => throw new HttpRequestException("offline");

        await app.Session.SignOutAsync();

        Assert.Equal(SessionState.SignedOut, app.Session.State);
        Assert.Null(app.TokenStore.Stored);
    }

    [Fact]
    public async Task AccessToken_StillValid_IsReturnedWithoutRefreshing()
    {
        app.TokenStore.Stored = ClientHarness.NewTokens(expiresAt: app.Time.GetUtcNow().AddMinutes(30));

        string? token = await app.Session.GetAccessTokenAsync();

        Assert.Equal("access-1", token);
        Assert.Equal(0, app.Oidc.RefreshCalls);
    }

    [Fact]
    public async Task AccessToken_AboutToExpire_IsRefreshedAndThePersistedSessionUpdated()
    {
        app.TokenStore.Stored = ClientHarness.NewTokens(expiresAt: app.Time.GetUtcNow().AddSeconds(30));
        app.Oidc.OnRefresh = current => Task.FromResult(AuthResult.Success(current with
        {
            AccessToken = "access-2",
            RefreshToken = "refresh-2",
            AccessTokenExpiresAt = app.Time.GetUtcNow().AddHours(1)
        }));

        string? token = await app.Session.GetAccessTokenAsync();

        Assert.Equal("access-2", token);
        Assert.Equal("refresh-2", app.TokenStore.Stored!.RefreshToken); // refresh tokens are single-use, so the new one must be kept
        Assert.Equal("Dana Quinn", app.Session.User!.Name);
    }

    [Fact]
    public async Task AccessToken_WhenTheServerRefusesTheRefreshToken_NeedsAFreshSignInButKeepsTheData()
    {
        app.TokenStore.Stored = ClientHarness.NewTokens(expiresAt: app.Time.GetUtcNow().AddSeconds(-5));
        app.Oidc.OnRefresh = _ => Task.FromResult(AuthResult.Failed(AuthFailure.Rejected, "invalid_grant"));
        await app.AddNoteAsync("Keep me");

        string? token = await app.Session.GetAccessTokenAsync();

        Assert.Null(token);
        Assert.Equal(SessionState.ReauthRequired, app.Session.State);
        Assert.NotNull(app.Session.User);
        Assert.Single(await app.Notes.SearchAsync(null));
    }

    [Theory]
    [InlineData(AuthFailure.Unreachable)]
    [InlineData(AuthFailure.Other)]
    public async Task AccessToken_WhenTheRefreshFailsForAnyOtherReason_StaysSignedIn(AuthFailure failure)
    {
        app.TokenStore.Stored = ClientHarness.NewTokens(expiresAt: app.Time.GetUtcNow().AddSeconds(-5));
        app.Oidc.OnRefresh = _ => Task.FromResult(AuthResult.Failed(failure));

        string? token = await app.Session.GetAccessTokenAsync();

        Assert.Null(token);
        Assert.Equal(SessionState.SignedIn, app.Session.State);
    }

    [Fact]
    public async Task AccessToken_WithNoRefreshToken_NeedsAFreshSignIn()
    {
        app.TokenStore.Stored = ClientHarness.NewTokens(expiresAt: app.Time.GetUtcNow().AddSeconds(-5), refreshToken: null);

        string? token = await app.Session.GetAccessTokenAsync();

        Assert.Null(token);
        Assert.Equal(SessionState.ReauthRequired, app.Session.State);
        Assert.Equal(0, app.Oidc.RefreshCalls);
    }

    [Fact]
    public async Task AccessToken_ManyCallersAtOnce_ShareASingleRefresh()
    {
        app.TokenStore.Stored = ClientHarness.NewTokens(expiresAt: app.Time.GetUtcNow().AddSeconds(-5));
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Oidc.OnRefresh = async current =>
        {
            await release.Task;
            return AuthResult.Success(current with { AccessToken = "access-2", AccessTokenExpiresAt = app.Time.GetUtcNow().AddHours(1) });
        };

        Task<string?>[] callers = Enumerable.Range(0, 5).Select(_ => app.Session.GetAccessTokenAsync()).ToArray();
        release.SetResult();
        string?[] tokens = await Task.WhenAll(callers);

        Assert.All(tokens, token => Assert.Equal("access-2", token));
        Assert.Equal(1, app.Oidc.RefreshCalls);
    }

    [Fact]
    public async Task AccessToken_AfterAReauthRequiredSessionSignsInAgain_WorksNormally()
    {
        app.TokenStore.Stored = ClientHarness.NewTokens(expiresAt: app.Time.GetUtcNow().AddSeconds(-5));
        app.Oidc.OnRefresh = _ => Task.FromResult(AuthResult.Failed(AuthFailure.Rejected));
        await app.Session.GetAccessTokenAsync();
        Assert.Equal(SessionState.ReauthRequired, app.Session.State);

        app.Oidc.OnSignIn = () => AuthResult.Success(ClientHarness.NewTokens("user-1", app.Time.GetUtcNow().AddHours(1)));
        await app.Session.SignInAsync();

        Assert.Equal(SessionState.SignedIn, app.Session.State);
        Assert.Equal("access-1", await app.Session.GetAccessTokenAsync());
    }
}
