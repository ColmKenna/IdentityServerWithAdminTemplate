#if DEBUG
using System.Net.Security;

namespace IdentityServerProject.Mobile.Services;

/// <summary>
///     Debug builds only. Phones and emulators do not trust the ASP.NET Core development certificate that
///     <c>https://localhost:5001</c> presents, so sign-in and sync would fail the TLS handshake. This accepts that
///     one certificate for localhost and nothing else; every other connection is validated as normal.
/// </summary>
internal static class DevelopmentCertificate
{
    public static HttpMessageHandler CreateHandler() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (request, certificate, _, errors) =>
            errors == SslPolicyErrors.None
            || (request.RequestUri?.IsLoopback == true && certificate?.Issuer == "CN=localhost")
    };
}
#endif
