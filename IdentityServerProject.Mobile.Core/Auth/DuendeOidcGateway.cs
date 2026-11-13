using System.Security.Claims;
using Duende.IdentityModel;
using Duende.IdentityModel.Client;
using Duende.IdentityModel.OidcClient;
using Duende.IdentityModel.OidcClient.Browser;
using Duende.IdentityModel.OidcClient.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdentityServerProject.Mobile.Core.Auth;

/// <summary>
///     Authorization code + PKCE against IdentityServer with the system browser, as RFC 8252 requires of a
///     native app. The app is a public client: there is no secret to embed, and the only long-lived
///     credential, the refresh token, never leaves the OS credential store (see <see cref="ISecureTokenStore" />).
/// </summary>
public sealed class DuendeOidcGateway : IOidcGateway
{
    public const string HttpClientName = "oidc";

    // Error codes from the token endpoint that mean the refresh token itself is no good, as opposed
    // to a transient fault. Anything not listed is treated as transient, which keeps the user signed in.
    private static readonly HashSet<string> RefusedGrantErrors =
        ["invalid_grant", "invalid_client", "unauthorized_client", "invalid_token"];

    private readonly MobileClientOptions options;
    private readonly IHttpClientFactory httpClients;
    private readonly Lazy<OidcClient> client;

    public DuendeOidcGateway(
        IOptions<MobileClientOptions> options,
        IBrowser browser,
        IHttpClientFactory httpClients,
        ILoggerFactory loggerFactory)
    {
        this.options = options.Value;
        this.httpClients = httpClients;
        client = new Lazy<OidcClient>(() => new OidcClient(new OidcClientOptions
        {
            Authority = this.options.Authority,
            ClientId = this.options.ClientId,
            Scope = this.options.Scope,
            RedirectUri = this.options.RedirectUri,
            PostLogoutRedirectUri = this.options.PostLogoutRedirectUri,
            Browser = browser,
            LoggerFactory = loggerFactory,
            HttpClientFactory = _ => httpClients.CreateClient(HttpClientName)
        }));
    }

    public async Task<AuthResult> SignInAsync(CancellationToken cancellationToken)
    {
        LoginResult result;
        try
        {
            result = await client.Value.LoginAsync(
                new LoginRequest
                {
                    // Always ask for credentials. The system browser keeps its own IdentityServer session, and
                    // without this a second person tapping "Sign in" on a shared phone would be signed in as
                    // the first without typing anything.
                    FrontChannelExtraParameters = Parameters.FromObject(new { prompt = "login" })
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return AuthResult.Failed(AuthFailure.Cancelled);
        }
        catch (Exception ex) when (IsUnreachable(ex))
        {
            return AuthResult.Failed(AuthFailure.Unreachable, "Couldn't reach the sign-in server.");
        }

        if (result.IsError) return Classify(result.Error, result.ErrorDescription);

        return AuthResult.Success(new TokenSet(
            result.AccessToken,
            result.RefreshToken,
            result.IdentityToken,
            result.AccessTokenExpiration,
            ToProfile(result.User)));
    }

    public async Task<AuthResult> RefreshAsync(TokenSet current, CancellationToken cancellationToken)
    {
        if (current.RefreshToken is null) return AuthResult.Failed(AuthFailure.Rejected, "No refresh token.");

        RefreshTokenResult result;
        try
        {
            result = await client.Value.RefreshTokenAsync(current.RefreshToken, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (IsUnreachable(ex) || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return AuthResult.Failed(AuthFailure.Unreachable, "Couldn't reach the sign-in server.");
        }

        if (result.IsError)
            return result.Error is { } error && RefusedGrantErrors.Contains(error)
                ? AuthResult.Failed(AuthFailure.Rejected, result.ErrorDescription ?? error)
                : Classify(result.Error, result.ErrorDescription);

        return AuthResult.Success(current with
        {
            AccessToken = result.AccessToken,

            // Refresh tokens are one-time use: the response carries the replacement, and the old one is dead.
            RefreshToken = result.RefreshToken ?? current.RefreshToken,
            IdentityToken = result.IdentityToken ?? current.IdentityToken,
            AccessTokenExpiresAt = result.AccessTokenExpiration
        });
    }

    public async Task SignOutAsync(TokenSet? tokens, CancellationToken cancellationToken)
    {
        if (tokens is null) return;

        if (tokens.RefreshToken is not null) await RevokeRefreshTokenAsync(tokens.RefreshToken, cancellationToken);

        // Ends the identity server's browser session so the next sign-in has to authenticate again.
        await client.Value.LogoutAsync(new LogoutRequest { IdTokenHint = tokens.IdentityToken }, cancellationToken);
    }

    private async Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using HttpClient http = httpClients.CreateClient(HttpClientName);
        DiscoveryDocumentResponse discovery = await http.GetDiscoveryDocumentAsync(options.Authority, cancellationToken);
        if (discovery.IsError || discovery.RevocationEndpoint is null) return;

        await http.RevokeTokenAsync(
            new TokenRevocationRequest
            {
                Address = discovery.RevocationEndpoint,
                ClientId = options.ClientId,
                Token = refreshToken,
                TokenTypeHint = OidcConstants.TokenTypes.RefreshToken,

                // A public client has no secret to put in a Basic header; the identity server then needs the
                // client id in the form body to know which client is revoking.
                ClientCredentialStyle = ClientCredentialStyle.PostBody
            },
            cancellationToken);
    }

    /// <summary>
    ///     OidcClient reports a failed discovery request by throwing InvalidOperationException (with the
    ///     HttpRequestException inside), not by returning an error, so a dead connection looks like a bug unless caught.
    /// </summary>
    private static bool IsUnreachable(Exception ex) =>
        ex is HttpRequestException
        || ex is InvalidOperationException { InnerException: HttpRequestException }
        || ex is InvalidOperationException { Message: { } message } && message.StartsWith("Error loading discovery document", StringComparison.Ordinal);

    private static AuthResult Classify(string? error, string? description) => error switch
    {
        "UserCancel" => AuthResult.Failed(AuthFailure.Cancelled),
        not null when error.StartsWith("Error loading discovery document", StringComparison.Ordinal) =>
            AuthResult.Failed(AuthFailure.Unreachable, "Couldn't reach the sign-in server."),
        _ => AuthResult.Failed(AuthFailure.Other, description ?? error ?? "Sign-in failed.")
    };

    private static UserProfile ToProfile(ClaimsPrincipal user)
    {
        string subject = user.FindFirst("sub")?.Value
                         ?? throw new InvalidOperationException("The identity token has no subject.");
        string? email = user.FindFirst("email")?.Value;

        return new UserProfile(
            subject,
            user.FindFirst("name")?.Value ?? email ?? subject,
            email,
            user.FindAll("role").Select(c => c.Value).Distinct().ToArray());
    }
}
