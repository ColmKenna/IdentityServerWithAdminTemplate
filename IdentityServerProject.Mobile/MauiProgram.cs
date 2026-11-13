using IOidcBrowser = Duende.IdentityModel.OidcClient.Browser.IBrowser;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using IdentityServerProject.Mobile.Core;
using IdentityServerProject.Mobile.Core.Auth;
using IdentityServerProject.Mobile.Core.Sync;
using IdentityServerProject.Mobile.Services;

namespace IdentityServerProject.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // Shipped inside the app. It holds addresses, never secrets: this is a public client, so there is nothing
        // secret to hold. Change Mobile:Authority in appsettings.json to point a build at another identity server.
        using (Stream settings = typeof(MauiProgram).Assembly.GetManifestResourceStream("appsettings.json")
                                 ?? throw new InvalidOperationException("appsettings.json is not embedded in the app."))
            builder.Configuration.AddJsonStream(settings);

        builder.Services.AddMauiBlazorWebView();

        // What only the platform can provide.
        builder.Services.AddSingleton<ISecureStringStorage, MauiSecureStringStorage>();
        builder.Services.AddSingleton<INetworkStatus, MauiNetworkStatus>();
        builder.Services.AddSingleton<IOidcBrowser, MauiAuthBrowser>();
        builder.Services.AddSingleton<IMauiInitializeService, SyncStartup>();

        builder.Services.AddMobileApp(options =>
        {
            builder.Configuration.GetSection(MobileClientOptions.SectionName).Bind(options);
            options.DatabasePath = Path.Combine(FileSystem.AppDataDirectory, "mobile-app.db3");
        });

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();

        // The ASP.NET Core development certificate is not trusted by phones or emulators. Accepting it is
        // confined to Debug builds and to localhost; a Release build validates certificates normally.
        builder.Services.ConfigureHttpClientDefaults(http =>
            http.ConfigurePrimaryHttpMessageHandler(DevelopmentCertificate.CreateHandler));
#endif

        return builder.Build();
    }
}
