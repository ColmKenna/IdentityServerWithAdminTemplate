using System.Net;
using System.Net.Http.Headers;
using System.Text;
using IdentityServerProject.Mobile.Core.Auth;
using IdentityServerProject.Mobile.Core.Sync;
using IdentityServerProject.Mobile.Core.Sync.Contract;
using IdentityServerProject.Mobile.Core.Tests.Infrastructure;

namespace IdentityServerProject.Mobile.Core.Tests.Sync;

/// <summary>The real HTTP client and its bearer-token handler, with only the server's end stubbed.</summary>
public sealed class HttpSyncApiTests
{
    private sealed class StubServer : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, AuthenticationHeaderValue? Authorization, string Body)> Requests { get; } = [];
        public Func<HttpResponseMessage> Respond { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "results": [] }""", Encoding.UTF8, "application/json")
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!, request.Headers.Authorization,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return Respond();
        }
    }

    private static ClientHarness NewApp(StubServer server) => new(configureServices: services =>
    {
        services.AddSingleton<ISyncApi, HttpSyncApi>();
        services.AddHttpClient(HttpSyncApi.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => server);
    });

    private static Task SignInAsync(ClientHarness app)
    {
        app.TokenStore.Stored = ClientHarness.NewTokens(expiresAt: app.Time.GetUtcNow().AddHours(1));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Push_PostsJsonToTheSyncEndpoint_WithTheBearerToken()
    {
        StubServer server = new();
        await using ClientHarness app = NewApp(server);
        await SignInAsync(app);
        ISyncApi api = app.Services.GetRequiredService<ISyncApi>();
        SyncRecord record = new(SyncEntityKind.Note, Guid.NewGuid(), false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        PushResponse response = await api.PushAsync(new PushRequest([record]), CancellationToken.None);

        var request = Assert.Single(server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://idp.test/api/sync/push", request.Uri.ToString());
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "access-1"), request.Authorization);
        Assert.Contains("\"kind\":\"Note\"", request.Body);
        Assert.Empty(response.Results);
    }

    [Fact]
    public async Task Pull_EscapesTheCursorAndAsksForAPageSize()
    {
        StubServer server = new()
        {
            Respond = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "changes": [], "nextCursor": "n&x", "hasMore": false }""", Encoding.UTF8, "application/json")
            }
        };
        await using ClientHarness app = NewApp(server);
        await SignInAsync(app);

        PullResponse response = await app.Services.GetRequiredService<ISyncApi>().PullAsync("a b&c=d", 50, CancellationToken.None);

        Assert.Equal("https://idp.test/api/sync/pull?limit=50&cursor=a%20b%26c%3Dd", server.Requests.Single().Uri.AbsoluteUri);
        Assert.Equal("n&x", response.NextCursor);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ARefusedToken_IsReportedAsAnAuthenticationProblem(HttpStatusCode status)
    {
        StubServer server = new() { Respond = () => new HttpResponseMessage(status) };
        await using ClientHarness app = NewApp(server);
        await SignInAsync(app);

        await Assert.ThrowsAsync<SyncAuthenticationException>(() =>
            app.Services.GetRequiredService<ISyncApi>().PullAsync(null, 10, CancellationToken.None));
    }

    [Fact]
    public async Task AServerError_IsAnOrdinaryFailure()
    {
        StubServer server = new() { Respond = () => new HttpResponseMessage(HttpStatusCode.BadGateway) };
        await using ClientHarness app = NewApp(server);
        await SignInAsync(app);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            app.Services.GetRequiredService<ISyncApi>().PullAsync(null, 10, CancellationToken.None));
    }

    [Fact]
    public async Task WithoutAUsableToken_NothingIsSentToTheServer()
    {
        StubServer server = new();
        await using ClientHarness app = NewApp(server); // nobody signed in

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            app.Services.GetRequiredService<ISyncApi>().PullAsync(null, 10, CancellationToken.None));

        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task WhenTheSessionNeedsANewSignIn_ItIsReportedAsAnAuthenticationProblem_AndNothingIsSent()
    {
        StubServer server = new();
        await using ClientHarness app = NewApp(server);
        app.TokenStore.Stored = ClientHarness.NewTokens(expiresAt: app.Time.GetUtcNow().AddSeconds(-5));
        app.Oidc.OnRefresh = _ => Task.FromResult(AuthResult.Failed(AuthFailure.Rejected));

        await Assert.ThrowsAsync<SyncAuthenticationException>(() =>
            app.Services.GetRequiredService<ISyncApi>().PullAsync(null, 10, CancellationToken.None));

        Assert.Empty(server.Requests);
    }
}
