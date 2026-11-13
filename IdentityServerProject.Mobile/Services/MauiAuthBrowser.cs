using Duende.IdentityModel.OidcClient.Browser;
using IOidcBrowser = Duende.IdentityModel.OidcClient.Browser.IBrowser;

namespace IdentityServerProject.Mobile.Services;

/// <summary>
///     Hands OidcClient's sign-in and sign-out URLs to the system browser (Chrome Custom Tabs on Android,
///     ASWebAuthenticationSession on iOS) and returns the URL the browser was redirected back to. The app never
///     sees the user's password: it is typed into the identity server's own page.
/// </summary>
internal sealed class MauiAuthBrowser : IOidcBrowser
{
    public async Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            WebAuthenticatorResult result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = new Uri(options.StartUrl),
                CallbackUrl = new Uri(options.EndUrl),

                // iOS only: a private session, so the browser does not keep the identity server's cookie.
                PrefersEphemeralWebBrowserSession = true
            });

            return new BrowserResult
            {
                ResultType = BrowserResultType.Success,
                Response = ToResponseUrl(options.EndUrl, result)
            };
        }
        catch (TaskCanceledException)
        {
            // WebAuthenticator reports "the user closed the browser" this way.
            return new BrowserResult { ResultType = BrowserResultType.UserCancel };
        }
        catch (Exception ex)
        {
            return new BrowserResult { ResultType = BrowserResultType.UnknownError, Error = ex.Message };
        }
    }

    /// <summary>
    ///     WebAuthenticator splits the redirect into key/value pairs; OidcClient wants the URL back.
    ///     The authorization code flow returns its parameters in the query string.
    /// </summary>
    private static string ToResponseUrl(string redirectUri, WebAuthenticatorResult result)
    {
        string query = string.Join("&", result.Properties.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return query.Length == 0 ? redirectUri : $"{redirectUri}?{query}";
    }
}
