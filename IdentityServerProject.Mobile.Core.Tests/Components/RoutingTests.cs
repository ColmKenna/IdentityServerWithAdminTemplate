using Bunit;
using Microsoft.AspNetCore.Components;
using IdentityServerProject.Mobile.Core.Auth;
using IdentityServerProject.Mobile.Core.Components;
using IdentityServerProject.Mobile.Core.Sync;
using IdentityServerProject.Mobile.Core.Tests.Infrastructure;

namespace IdentityServerProject.Mobile.Core.Tests.Components;

/// <summary>The whole app shell: router, authorization, layout and the sync notices, as a user would meet them.</summary>
public sealed class RoutingTests : ClientComponentContext
{
    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void AnAnonymousVisitor_IsSentToSignIn_NoMatterWhichScreenTheyAskFor()
    {
        Navigation.NavigateTo("notes");

        IRenderedComponent<Routes> app = Render<Routes>();

        app.WaitForAssertion(() => Assert.EndsWith("/signin", Navigation.Uri));
        Assert.Empty(app.FindAll("nav.bottom-nav"));
    }

    [Fact]
    public async Task ASignedInUser_SeesTheRequestedScreenAndTheNavigation()
    {
        await SignInAsync();
        Navigation.NavigateTo("notes");

        IRenderedComponent<Routes> app = Render<Routes>();

        app.WaitForAssertion(() => Assert.NotEmpty(app.FindAll("input.search")));
        Assert.NotEmpty(app.FindAll("nav.bottom-nav"));
        Assert.EndsWith("/notes", Navigation.Uri);
    }

    [Fact]
    public async Task OfflineAtLaunch_TheAppStillOpensWithTheUsersData_AndSaysItIsOffline()
    {
        // The state of the phone after being signed in once, then losing the connection.
        await SignInAsync();
        await App.AddNoteAsync("Already on the phone");
        App.Network.IsOnline = false;
        await App.Coordinator.SyncNowAsync();
        Navigation.NavigateTo("notes");

        IRenderedComponent<Routes> app = Render<Routes>();

        app.WaitForAssertion(() => Assert.Contains("Already on the phone", app.Markup));
        Assert.Contains("You're offline", app.Markup);
        Assert.Contains("1 change(s)", app.Markup);
        Assert.Equal(0, App.Oidc.RefreshCalls); // opening the app offline never needed the identity server
    }

    [Fact]
    public async Task AnExpiredSignIn_KeepsTheDataVisible_AndOffersToSignInAgain()
    {
        App.TokenStore.Stored = ClientHarness.NewTokens(expiresAt: App.Time.GetUtcNow().AddSeconds(-5));
        App.Oidc.OnRefresh = _ => Task.FromResult(AuthResult.Failed(AuthFailure.Rejected));
        await App.Session.InitializeAsync();
        await App.AddNoteAsync("Still mine");
        await App.Session.GetAccessTokenAsync(); // what the first real sync request would do
        await App.Coordinator.SyncNowAsync();
        Navigation.NavigateTo("notes");

        IRenderedComponent<Routes> app = Render<Routes>();

        app.WaitForAssertion(() => Assert.Contains("Still mine", app.Markup));
        Assert.Equal(SyncState.AuthRequired, App.Coordinator.Status.State);
        Assert.Contains("sign in again", app.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnknownAddress_ShowsTheNotFoundScreen()
    {
        Navigation.NavigateTo("definitely/not/a/page");

        IRenderedComponent<Routes> app = Render<Routes>();

        app.WaitForAssertion(() => Assert.Contains("That page doesn't exist", app.Markup));
    }
}
