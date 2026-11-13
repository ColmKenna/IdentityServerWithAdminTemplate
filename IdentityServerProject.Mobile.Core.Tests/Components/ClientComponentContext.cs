using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using IdentityServerProject.Mobile.Core.Auth;
using IdentityServerProject.Mobile.Core.Data;
using IdentityServerProject.Mobile.Core.Services;
using IdentityServerProject.Mobile.Core.Sync;
using IdentityServerProject.Mobile.Core.Tests.Infrastructure;

namespace IdentityServerProject.Mobile.Core.Tests.Components;

/// <summary>
///     A bUnit context whose services are the real client services (and real SQLite file) from
///     <see cref="ClientHarness" />, so a page test exercises the page, the service and the database together.
/// </summary>
public abstract class ClientComponentContext : BunitContext, IAsyncLifetime
{
    protected ClientComponentContext()
    {
        App = new ClientHarness();

        Forward<INoteService>();
        Forward<ISessionService>();
        Forward<SyncCoordinator>();
        Forward<LocalDataChanges>();
        Forward<TimeProvider>();
        Forward<IOptions<MobileClientOptions>>();

        // bUnit pre-registers an authorization service that throws unless a test opts into its fake one.
        // These tests want the real policy evaluation against the real session, so it is replaced.
        Services.AddAuthorizationCore();
        Services.AddSingleton<IAuthorizationService, DefaultAuthorizationService>();
        Services.AddCascadingAuthenticationState();
        Services.AddScoped<AuthenticationStateProvider>(provider =>
            new SessionAuthenticationStateProvider(provider.GetRequiredService<ISessionService>()));
    }

    protected ClientHarness App { get; }

    protected async Task SignInAsync(string subject = "user-1")
    {
        App.TokenStore.Stored = ClientHarness.NewTokens(subject, App.Time.GetUtcNow().AddHours(1));
        await App.Session.InitializeAsync();
    }

    private void Forward<T>() where T : class => Services.AddSingleton(_ => App.Services.GetRequiredService<T>());

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    // xUnit 2 never calls IAsyncDisposable on a test class, so overriding BunitContext.DisposeAsyncCore leaves
    // the harness undisposed and its SQLite file behind in the temp folder; IAsyncLifetime it does call. The
    // rendered components go first because they may still be reading the database.
    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await App.DisposeAsync();
    }
}
