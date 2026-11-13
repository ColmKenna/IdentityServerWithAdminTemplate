using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using IdentityServerProject.Mobile.Core;
using IdentityServerProject.Mobile.Core.Auth;
using IdentityServerProject.Mobile.Core.Data;
using IdentityServerProject.Mobile.Core.Services;
using IdentityServerProject.Mobile.Core.Sync;

namespace Microsoft.Extensions.DependencyInjection;

public static class MobileAppServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the app's local data, sign-in and sync. The host must also register the platform
    ///     pieces: <see cref="ISecureStringStorage" />, <see cref="INetworkStatus" /> and Duende's
    ///     <c>IBrowser</c>. Logging and <c>IHttpClientFactory</c> are registered here if the host has not.
    /// </summary>
    public static IServiceCollection AddMobileApp(this IServiceCollection services, Action<MobileClientOptions> configure)
    {
        services.AddOptions<MobileClientOptions>().Configure(configure);
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);

        // Local data. Singletons: they hold no per-request state, and every context is short-lived.
        services.AddDbContextFactory<MobileDbContext>((provider, db) =>
            db.UseSqlite($"Data Source={provider.GetRequiredService<IOptions<MobileClientOptions>>().Value.DatabasePath}"));
        services.AddSingleton<LocalDatabase>();
        services.AddSingleton<LocalDataChanges>();
        services.AddSingleton<INoteService, NoteService>();

        // Sign-in.
        services.AddHttpClient(DuendeOidcGateway.HttpClientName);
        services.AddSingleton<IOidcGateway, DuendeOidcGateway>();
        services.AddSingleton<ISecureTokenStore, SecureStringTokenStore>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, SessionAuthenticationStateProvider>();

        // Sync.
        services.AddTransient<AccessTokenHandler>();
        services.AddHttpClient(HttpSyncApi.HttpClientName, (provider, client) =>
            {
                MobileClientOptions options = provider.GetRequiredService<IOptions<MobileClientOptions>>().Value;
                client.BaseAddress = options.ResolveApiBaseAddress();
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<AccessTokenHandler>();
        services.AddSingleton<ISyncApi, HttpSyncApi>();
        services.AddSingleton<SyncEngine>();
        services.AddSingleton<SyncCoordinator>();

        return services;
    }
}
