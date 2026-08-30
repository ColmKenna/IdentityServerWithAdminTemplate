using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using Duende.IdentityServer.Models;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientsTokenSettingsIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private HttpClient CreateClient(IClientTokenSettingsService clientDetailsService, bool allowAutoRedirect = true)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => { services.AddSingleton(clientDetailsService); });
        });
        _disposables.Add(factory);

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
    }

    private static ClientTokenSettingsModel SampleSettings(string id = "test-client") => new()
    {
        ClientId = ClientId.Create(id),
        ClientName = "Co-op Market Razor Client",
        AccessTokenLifetime = TokenLifetime.FromSeconds(3600),
        IdentityTokenLifetime = TokenLifetime.FromSeconds(300),
        RequireConsent = false,
        AllowOfflineAccess = true
    };

    private static IClientTokenSettingsService MockService(ClientTokenSettingsModel? details = null,
        bool updateSuccess = true)
    {
        var mock = new Mock<IClientTokenSettingsService>();
        mock.Setup(s => s.GetClientTokenSettingsAsync(It.IsAny<ClientId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientId id, CancellationToken _) =>
                id.Value == "non-existent" ? null : details ?? SampleSettings(id.Value));
        mock.Setup(s => s.UpdateClientTokenSettingsAsync(It.IsAny<ClientId>(),
                It.IsAny<ClientTokenSettingsInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientId id, ClientTokenSettingsInputModel _, CancellationToken _) =>
                id.Value == "non-existent"
                    ? AdminMutationResult.NotFoundResult()
                    : updateSuccess
                        ? AdminMutationResult.Success()
                        : AdminMutationResult.ValidationFailure("Input", "Update failed."));
        return mock.Object;
    }

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    private static async Task<(string Token, string Cookie)> ExtractAntiForgeryTokenAndCookieAsync(HttpClient client,
        string pageUrl)
    {
        HttpResponseMessage getResponse = await client.GetAsync(pageUrl);
        getResponse.EnsureSuccessStatusCode();

        IDocument document = await GetDocumentAsync(getResponse);
        IElement? tokenInput = document.QuerySelector("input[name='__RequestVerificationToken']");
        string token = tokenInput?.GetAttribute("value") ?? string.Empty;

        IEnumerable<string> setCookieHeaders = getResponse.Headers.GetValues("Set-Cookie");
        string cookieHeader = string.Join("; ", setCookieHeaders.Select(h => h.Split(';')[0]));

        return (token, cookieHeader);
    }

    [Fact]
    public async Task Get_ExistingClient_Returns200WithForm()
    {
        HttpClient httpClient = CreateClient(MockService());

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Clients/TokenSettings/test-client");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IDocument document = await GetDocumentAsync(response);
        IElement? heading = document.QuerySelector("h1, h2");
        Assert.NotNull(heading);
        Assert.Contains("Token Settings", heading!.TextContent);
    }

    [Fact]
    public async Task Get_ExistingClient_PopulatesCurrentValues()
    {
        HttpClient httpClient = CreateClient(MockService());

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Clients/TokenSettings/test-client");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IDocument document = await GetDocumentAsync(response);

        string? accessLifetime =
            document.QuerySelector("input[name='Input.AccessTokenLifetime']")?.GetAttribute("value");
        string? identityLifetime =
            document.QuerySelector("input[name='Input.IdentityTokenLifetime']")?.GetAttribute("value");

        Assert.Equal("3600", accessLifetime);
        Assert.Equal("300", identityLifetime);
    }

    [Fact]
    public async Task Get_NonExistentClient_Returns404()
    {
        HttpClient httpClient = CreateClient(MockService());

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Clients/TokenSettings/non-existent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ValidInput_RedirectsToDetails()
    {
        var mock = new Mock<IClientTokenSettingsService>();
        mock.Setup(s => s.GetClientTokenSettingsAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSettings());
        mock.Setup(s => s.UpdateClientTokenSettingsAsync(ClientId.Create("test-client"),
                It.IsAny<ClientTokenSettingsInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/TokenSettings/test-client");

        var formValues = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Input.AccessTokenLifetime", "7200" },
            { "Input.IdentityTokenLifetime", "600" },
            { "Input.RequireConsent", "true" },
            { "Input.AllowOfflineAccess", "true" },
            { "Input.RefreshTokenUsage", "1" },
            { "Input.RefreshTokenExpiration", "1" },
            { "Input.AbsoluteRefreshTokenLifetime", "172800" },
            { "Input.SlidingRefreshTokenLifetime", "72000" }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/TokenSettings/test-client")
        {
            Content = new FormUrlEncodedContent(formValues)
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Clients/Details/test-client", response.Headers.Location?.OriginalString);

        mock.Verify(s => s.UpdateClientTokenSettingsAsync(
            ClientId.Create("test-client"),
            It.Is<ClientTokenSettingsInputModel>(m => m.AccessTokenLifetime == 7200
                                                      && m.IdentityTokenLifetime == 600
                                                      && m.RequireConsent
                                                      && m.AllowOfflineAccess
                                                      && m.RefreshToken != null
                                                      && m.RefreshToken.Usage == TokenUsage.OneTimeOnly
                                                      && m.RefreshToken.Expiration == TokenExpiration.Absolute
                                                      && m.RefreshToken.AbsoluteLifetime == 172800
                                                      && m.RefreshToken.SlidingLifetime == 72000),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_AccessTokenLifetimeOutOfRange_ReturnsValidationErrorAndDoesNotPersist()
    {
        var mock = new Mock<IClientTokenSettingsService>();
        mock.Setup(s => s.GetClientTokenSettingsAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSettings());

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/TokenSettings/test-client");

        var formValues = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Input.AccessTokenLifetime", "100000" }, // above the 86400 max
            { "Input.IdentityTokenLifetime", "300" }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/TokenSettings/test-client")
        {
            Content = new FormUrlEncodedContent(formValues)
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);
        IElement? summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);

        mock.Verify(
            s => s.UpdateClientTokenSettingsAsync(It.IsAny<ClientId>(), It.IsAny<ClientTokenSettingsInputModel>(),
                It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Post_IdentityTokenLifetimeOutOfRange_ReturnsValidationErrorAndDoesNotPersist()
    {
        var mock = new Mock<IClientTokenSettingsService>();
        mock.Setup(s => s.GetClientTokenSettingsAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSettings());

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/TokenSettings/test-client");

        var formValues = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Input.AccessTokenLifetime", "3600" },
            { "Input.IdentityTokenLifetime", "5000" } // above the 3600 max
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/TokenSettings/test-client")
        {
            Content = new FormUrlEncodedContent(formValues)
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);
        IElement? summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);

        mock.Verify(
            s => s.UpdateClientTokenSettingsAsync(It.IsAny<ClientId>(), It.IsAny<ClientTokenSettingsInputModel>(),
                It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Post_IdentityTokenLifetimeOutOfRange_ReturnsValidationErrorWithLifetimesTabActive()
    {
        HttpClient httpClient = CreateClient(MockService(), false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/TokenSettings/test-client");

        var formValues = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Input.AccessTokenLifetime", "3600" },
            { "Input.IdentityTokenLifetime", "5000" }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/TokenSettings/test-client")
        {
            Content = new FormUrlEncodedContent(formValues)
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IDocument document = await GetDocumentAsync(response);
        IElement? activeTab = document.QuerySelector("#client-token-settings-tabs > ck-tab[active]");
        Assert.NotNull(activeTab);
        Assert.Equal("Token Lifetimes", activeTab!.GetAttribute("label"));
    }

    [Fact]
    public async Task Post_NonExistentClient_ReturnsNotFound()
    {
        HttpClient httpClient = CreateClient(MockService(), false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/TokenSettings/test-client");

        var formValues = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Input.AccessTokenLifetime", "3600" },
            { "Input.IdentityTokenLifetime", "300" }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/TokenSettings/non-existent")
        {
            Content = new FormUrlEncodedContent(formValues)
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}