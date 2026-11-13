using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using IdentityServerProject.Mobile.Core.Auth;
using IdentityServerProject.Mobile.Core.Tests.Infrastructure;

namespace IdentityServerProject.Mobile.Core.Tests.Auth;

public sealed class TokenStoreAndAuthStateTests
{
    private sealed class MemoryStorage : ISecureStringStorage
    {
        public Dictionary<string, string> Values { get; } = [];
        public bool ThrowOnGet { get; set; }

        public Task<string?> GetAsync(string key) =>
            ThrowOnGet ? throw new InvalidOperationException("Keystore key lost.") : Task.FromResult(Values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }

        public void Remove(string key) => Values.Remove(key);
    }

    private static SecureStringTokenStore NewStore(MemoryStorage storage) => new(storage, NullLogger<SecureStringTokenStore>.Instance);

    [Fact]
    public async Task Tokens_SurviveARoundTrip_IncludingTheProfileUsedOffline()
    {
        SecureStringTokenStore store = NewStore(new MemoryStorage());
        TokenSet tokens = ClientHarness.NewTokens();

        await store.SaveAsync(tokens);
        TokenSet? loaded = await store.LoadAsync();

        Assert.Equal(tokens.AccessToken, loaded!.AccessToken);
        Assert.Equal(tokens.RefreshToken, loaded.RefreshToken);
        Assert.Equal(tokens.AccessTokenExpiresAt, loaded.AccessTokenExpiresAt);
        Assert.Equal(tokens.User.Subject, loaded.User.Subject);
        Assert.Equal(tokens.User.Roles, loaded.User.Roles);
    }

    [Fact]
    public async Task Load_WhenNothingIsStored_ReturnsNull()
    {
        Assert.Null(await NewStore(new MemoryStorage()).LoadAsync());
    }

    [Fact]
    public async Task Load_OfCorruptedData_MeansSignedOut_AndClearsTheBadValue()
    {
        MemoryStorage storage = new();
        storage.Values["mobile.tokens"] = "{ this is not json";

        Assert.Null(await NewStore(storage).LoadAsync());
        Assert.Empty(storage.Values);
    }

    [Fact]
    public async Task Load_WhenThePlatformStoreThrows_MeansSignedOutRatherThanACrash()
    {
        MemoryStorage storage = new() { ThrowOnGet = true };
        storage.Values["mobile.tokens"] = "anything";

        Assert.Null(await NewStore(storage).LoadAsync());
    }

    [Fact]
    public async Task Clear_RemovesTheStoredSession()
    {
        MemoryStorage storage = new();
        SecureStringTokenStore store = NewStore(storage);
        await store.SaveAsync(ClientHarness.NewTokens());

        await store.ClearAsync();

        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task AuthState_IsAnonymousWhenSignedOut()
    {
        await using ClientHarness app = new();
        using SessionAuthenticationStateProvider provider = new(app.Session);

        AuthenticationState state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task AuthState_IsAuthenticatedFromTheStoredProfile_WithNoNetworkNeeded()
    {
        await using ClientHarness app = new();
        app.TokenStore.Stored = ClientHarness.NewTokens("user-9");
        using SessionAuthenticationStateProvider provider = new(app.Session);

        AuthenticationState state = await provider.GetAuthenticationStateAsync();

        Assert.True(state.User.Identity!.IsAuthenticated);
        Assert.Equal("Dana Quinn", state.User.Identity.Name);
        Assert.Equal("user-9", state.User.FindFirst("sub")!.Value);
        Assert.True(state.User.IsInRole("Member"));
        Assert.Equal(0, app.Oidc.RefreshCalls);
    }

    [Fact]
    public async Task AuthState_StaysAuthenticatedWhileTheSessionNeedsANewSignIn()
    {
        await using ClientHarness app = new();
        app.TokenStore.Stored = ClientHarness.NewTokens(expiresAt: app.Time.GetUtcNow().AddSeconds(-5));
        app.Oidc.OnRefresh = _ => Task.FromResult(AuthResult.Failed(AuthFailure.Rejected));
        using SessionAuthenticationStateProvider provider = new(app.Session);
        await app.Session.GetAccessTokenAsync();

        AuthenticationState state = await provider.GetAuthenticationStateAsync();

        Assert.Equal(SessionState.ReauthRequired, app.Session.State);
        Assert.True(state.User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task AuthState_NotifiesWhenTheUserSignsInAndOut()
    {
        await using ClientHarness app = new();
        using SessionAuthenticationStateProvider provider = new(app.Session);
        await app.Session.InitializeAsync();
        List<ClaimsPrincipal> seen = [];
        provider.AuthenticationStateChanged += async task => seen.Add((await task).User);
        app.Oidc.OnSignIn = () => AuthResult.Success(ClientHarness.NewTokens());

        await app.Session.SignInAsync();
        await app.Session.SignOutAsync();

        Assert.Equal([true, false], seen.Select(user => user.Identity!.IsAuthenticated));
    }
}
