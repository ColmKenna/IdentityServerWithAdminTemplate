using System.Net;
using System.Text;
using Duende.IdentityModel.OidcClient.Browser;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using IdentityServerProject.Mobile.Core.Auth;
using IdentityServerProject.Mobile.Core.Tests.Infrastructure;

namespace IdentityServerProject.Mobile.Core.Tests.Auth;

/// <summary>
///     Runs the real OidcClient against a stub identity server, because the line between "the server
///     refused this refresh token" (sign in again) and "the server could not be reached" (carry on
///     working) is decided by what OidcClient reports, and getting it wrong either strands a working
///     user or hides a revoked one.
/// </summary>
public sealed class DuendeOidcGatewayTests
{
    private const string Authority = "https://idp.test";

    private sealed class StubIdentityServer : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> OnToken { get; set; } = _ => Json(HttpStatusCode.OK, "{}");
        public List<string> Requests { get; } = [];
        public List<string> RevocationBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");

            switch (request.RequestUri.AbsolutePath)
            {
                case "/.well-known/openid-configuration":
                    return Json(HttpStatusCode.OK, $$"""
                        {
                          "issuer": "{{Authority}}",
                          "jwks_uri": "{{Authority}}/.well-known/openid-configuration/jwks",
                          "authorization_endpoint": "{{Authority}}/connect/authorize",
                          "token_endpoint": "{{Authority}}/connect/token",
                          "userinfo_endpoint": "{{Authority}}/connect/userinfo",
                          "end_session_endpoint": "{{Authority}}/connect/endsession",
                          "revocation_endpoint": "{{Authority}}/connect/revocation",
                          "response_types_supported": ["code"],
                          "subject_types_supported": ["public"],
                          "id_token_signing_alg_values_supported": ["RS256"]
                        }
                        """);
                case "/.well-known/openid-configuration/jwks":
                    return Json(HttpStatusCode.OK, """{ "keys": [] }""");
                case "/connect/token":
                    return OnToken(request);
                case "/connect/revocation":
                    RevocationBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                    return new HttpResponseMessage(HttpStatusCode.OK);
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }
    }

    private sealed class RecordingBrowser : IBrowser
    {
        public List<string> StartUrls { get; } = [];

        public Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken)
        {
            StartUrls.Add(options.StartUrl);
            return Task.FromResult(new BrowserResult { ResultType = BrowserResultType.Success, Response = options.EndUrl });
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static DuendeOidcGateway NewGateway(HttpMessageHandler handler, IBrowser? browser = null) =>
        new(Options.Create(new MobileClientOptions { Authority = Authority }),
            browser ?? new RecordingBrowser(),
            new StubHttpClientFactory(handler),
            NullLoggerFactory.Instance);

    private static readonly TokenSet Current = ClientHarness.NewTokens();

    [Fact]
    public async Task Refresh_WhenTheServerRefusesTheRefreshToken_IsReportedAsRejected()
    {
        StubIdentityServer server = new() { OnToken = _ => Json(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""") };

        AuthResult result = await NewGateway(server).RefreshAsync(Current, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthFailure.Rejected, result.Failure);
    }

    [Fact]
    public async Task Refresh_WhenTheServerFailsInternally_IsNotReportedAsRejected()
    {
        StubIdentityServer server = new() { OnToken = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) };

        AuthResult result = await NewGateway(server).RefreshAsync(Current, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotEqual(AuthFailure.Rejected, result.Failure);
    }

    [Fact]
    public async Task Refresh_WhenTheConnectionFails_IsNotReportedAsRejected()
    {
        AuthResult result = await NewGateway(new ThrowingHandler()).RefreshAsync(Current, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotEqual(AuthFailure.Rejected, result.Failure);
    }

    [Fact]
    public async Task Refresh_Success_ReturnsTheRotatedTokensAndKeepsTheKnownProfile()
    {
        string? tokenRequestBody = null;
        StubIdentityServer server = new()
        {
            OnToken = request =>
            {
                tokenRequestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json(HttpStatusCode.OK, """
                    { "access_token": "access-2", "refresh_token": "refresh-2", "expires_in": 3600, "token_type": "Bearer" }
                    """);
            }
        };

        AuthResult result = await NewGateway(server).RefreshAsync(Current, CancellationToken.None);

        Assert.Contains("grant_type=refresh_token", tokenRequestBody);
        Assert.Contains("refresh_token=refresh-1", tokenRequestBody);
        Assert.Contains("client_id=mobile-app", tokenRequestBody); // public client: identified in the body, no secret
        Assert.DoesNotContain("client_secret", tokenRequestBody);
        Assert.True(result.Succeeded);
        Assert.Equal("access-2", result.Tokens!.AccessToken);
        Assert.Equal("refresh-2", result.Tokens.RefreshToken);
        Assert.Equal("id-1", result.Tokens.IdentityToken); // not re-issued by a refresh; the old one is still the logout hint
        Assert.Equal(Current.User, result.Tokens.User);
        Assert.True(result.Tokens.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(50));
    }

    [Fact]
    public async Task Refresh_WithoutARefreshToken_CannotBeAttempted()
    {
        StubIdentityServer server = new();

        AuthResult result = await NewGateway(server).RefreshAsync(Current with { RefreshToken = null }, CancellationToken.None);

        Assert.Equal(AuthFailure.Rejected, result.Failure);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task SignOut_RevokesTheRefreshTokenThenEndsTheBrowserSession()
    {
        StubIdentityServer server = new();
        RecordingBrowser browser = new();

        await NewGateway(server, browser).SignOutAsync(Current, CancellationToken.None);

        string revoked = Assert.Single(server.RevocationBodies);
        Assert.Contains("token=refresh-1", revoked);
        Assert.Contains("client_id=mobile-app", revoked);
        Assert.Contains("token_type_hint=refresh_token", revoked);

        string endSession = Assert.Single(browser.StartUrls);
        Assert.StartsWith($"{Authority}/connect/endsession", endSession);
        Assert.Contains("id_token_hint=id-1", endSession);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No route to host.");
    }
}
