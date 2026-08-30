using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientsSecretsIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private HttpClient CreateClient(IClientSecretsService clientDetailsService, bool allowAutoRedirect = true)
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

    private static ClientSecretsModel SampleSecrets(string id = "test-client", bool requireClientSecret = true,
        int secretCount = 2) => new()
    {
        ClientId = ClientId.Create(id),
        ClientName = "Co-op Market Razor Client",
        RequireClientSecret = requireClientSecret,
        Secrets = Enumerable.Range(1, secretCount)
            .Select(i => new ClientSecretSummary
                { Id = i, Description = $"Secret {i}", Created = DateTime.UtcNow.AddDays(-i) })
            .ToList()
    };

    private static IClientSecretsService MockService(
        ClientSecretsModel? details = null,
        ClientSecretGenerateResult? generateResult = null,
        ClientSecretRevokeResult? revokeResult = null)
    {
        var mock = new Mock<IClientSecretsService>();
        mock.Setup(s => s.GetClientSecretsAsync(It.IsAny<ClientId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientId id, CancellationToken _) =>
                id.Value == "non-existent" ? null : details ?? SampleSecrets(id.Value));
        mock.Setup(s => s.GenerateClientSecretAsync(It.IsAny<ClientId>(), It.IsAny<string>(), It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientId id, string? _, DateTime? _, CancellationToken _) => id.Value == "non-existent"
                ? ClientSecretGenerateResult.Failed("Client not found.")
                : generateResult ?? ClientSecretGenerateResult.Succeeded("plaintext-secret-value"));
        mock.Setup(s => s.RevokeClientSecretAsync(It.IsAny<ClientId>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientId id, int _, CancellationToken _) => id.Value == "non-existent"
                ? ClientSecretRevokeResult.Failed("Client not found.")
                : revokeResult ?? ClientSecretRevokeResult.Succeeded());
        return mock.Object;
    }

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    private static async Task<(string Token, string Cookie)> ExtractAntiForgeryTokenAndCookieAsync(
        HttpClient httpClient, string pageUrl)
    {
        HttpResponseMessage response = await httpClient.GetAsync(pageUrl);
        IDocument document = await GetDocumentAsync(response);

        var tokenInput = document.QuerySelector("input[name='__RequestVerificationToken']") as IHtmlInputElement;
        Assert.NotNull(tokenInput);

        string token = tokenInput!.Value;

        IEnumerable<string> cookies = response.Headers.GetValues("Set-Cookie");
        string? cookie = cookies.FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery"));
        Assert.NotNull(cookie);

        return (token, cookie!);
    }

    [Fact]
    public async Task Get_ExistingClient_Returns200OK_WithSecretsListedAndNoRevealBanner()
    {
        HttpClient httpClient = CreateClient(MockService());

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Clients/Secrets/test-client");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);
        Assert.Null(document.QuerySelector("#secret-reveal-banner"));
        Assert.Equal(2, document.QuerySelectorAll("[data-action='revoke-secret']").Length);
    }

    [Fact]
    public async Task Get_NonExistentClient_ReturnsNotFound()
    {
        HttpClient httpClient = CreateClient(MockService());

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Clients/Secrets/non-existent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_LastSecretOnConfidentialClient_RendersDisabledRevokeButton()
    {
        ClientSecretsModel details = SampleSecrets(requireClientSecret: true, secretCount: 1);
        HttpClient httpClient = CreateClient(MockService(details));

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Clients/Secrets/test-client");
        IDocument document = await GetDocumentAsync(response);

        var revokeButton = document.QuerySelector("[data-action='revoke-secret']") as IHtmlButtonElement;
        Assert.NotNull(revokeButton);
        Assert.True(revokeButton!.IsDisabled);
    }

    [Fact]
    public async Task Get_NotLastSecret_RendersEnabledRevokeButton()
    {
        ClientSecretsModel details = SampleSecrets(requireClientSecret: true, secretCount: 2);
        HttpClient httpClient = CreateClient(MockService(details));

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Clients/Secrets/test-client");
        IDocument document = await GetDocumentAsync(response);

        var revokeButtons = document.QuerySelectorAll("[data-action='revoke-secret']").OfType<IHtmlButtonElement>()
            .ToList();
        Assert.Equal(2, revokeButtons.Count);
        Assert.All(revokeButtons, b => Assert.False(b.IsDisabled));
    }

    [Fact]
    public async Task Get_LastSecretOnPublicClient_RendersEnabledRevokeButton()
    {
        ClientSecretsModel details = SampleSecrets(requireClientSecret: false, secretCount: 1);
        HttpClient httpClient = CreateClient(MockService(details));

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Clients/Secrets/test-client");
        IDocument document = await GetDocumentAsync(response);

        var revokeButton = document.QuerySelector("[data-action='revoke-secret']") as IHtmlButtonElement;
        Assert.NotNull(revokeButton);
        Assert.False(revokeButton!.IsDisabled);
    }

    [Fact]
    public async Task PostGenerate_ThenGet_ShowsRevealBannerOnce()
    {
        var mock = new Mock<IClientSecretsService>();
        mock.Setup(s => s.GetClientSecretsAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSecrets());
        mock.Setup(s => s.GenerateClientSecretAsync(ClientId.Create("test-client"), It.IsAny<string>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientSecretGenerateResult.Succeeded("brand-new-plaintext-secret"));

        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);
        WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => services.AddSingleton(mock.Object));
        });
        _disposables.Add(factory);
        HttpClient httpClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = true
        });

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Secrets/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("Rotated secret"), "Description");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Secrets/test-client?handler=Generate")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage generateResponse = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode); // auto-redirect followed

        IDocument afterGenerateDocument = await GetDocumentAsync(generateResponse);
        var revealInput = afterGenerateDocument.QuerySelector("#generated-secret-input") as IHtmlInputElement;
        Assert.NotNull(revealInput);
        Assert.Equal("brand-new-plaintext-secret", revealInput!.Value);

        // Subsequent GET must not repeat the reveal banner (TempData is consumed).
        HttpResponseMessage secondGet = await httpClient.GetAsync("/Admin/Clients/Secrets/test-client");
        IDocument secondDocument = await GetDocumentAsync(secondGet);
        Assert.Null(secondDocument.QuerySelector("#secret-reveal-banner"));
    }

    [Fact]
    public async Task PostGenerate_NonExistentClient_ReturnsNotFound()
    {
        HttpClient httpClient = CreateClient(MockService(), false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Secrets/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Secrets/non-existent?handler=Generate")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostGenerate_OverlongDescription_ReturnsSamePageWithFieldErrorAndDoesNotGenerate()
    {
        var mock = new Mock<IClientSecretsService>();
        mock.Setup(s => s.GetClientSecretsAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSecrets());

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Secrets/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(
            new StringContent(new string('x', ValidationConstants.MaxClientSecretDescriptionLength + 1)),
            "Description");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Secrets/test-client?handler=Generate")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IDocument document = await GetDocumentAsync(response);
        IElement? error = document.QuerySelector("[data-valmsg-for='Description']");
        Assert.NotNull(error);
        Assert.Contains(ValidationConstants.MaxClientSecretDescriptionLength.ToString(), error!.TextContent);
        mock.Verify(
            s => s.GenerateClientSecretAsync(It.IsAny<ClientId>(), It.IsAny<string>(), It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PostRevoke_Allowed_RedirectsAndRemovesSecret()
    {
        var mock = new Mock<IClientSecretsService>();
        mock.Setup(s => s.GetClientSecretsAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSecrets());
        mock.Setup(s => s.RevokeClientSecretAsync(ClientId.Create("test-client"), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientSecretRevokeResult.Succeeded());

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Secrets/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("1"), "secretId");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Secrets/test-client?handler=Revoke")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        mock.Verify(s => s.RevokeClientSecretAsync(ClientId.Create("test-client"), 1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PostRevoke_BlockedLastSecret_RedirectsWithErrorAndDoesNotRemove()
    {
        string blockReason =
            "This is the last secret on a confidential client and cannot be revoked. Generate a replacement secret first, or disable the client's secret requirement.";
        var mock = new Mock<IClientSecretsService>();
        mock.Setup(s => s.GetClientSecretsAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSecrets(secretCount: 1));
        mock.Setup(s => s.RevokeClientSecretAsync(ClientId.Create("test-client"), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientSecretRevokeResult.Failed(blockReason));

        HttpClient httpClient = CreateClient(mock.Object);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Secrets/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("1"), "secretId");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Secrets/test-client?handler=Revoke")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // followed redirect back to same page

        IDocument document = await GetDocumentAsync(response);
        IElement? errorAlert = document.QuerySelector(".alert-error");
        Assert.NotNull(errorAlert);
        Assert.Contains("last secret", errorAlert!.TextContent, StringComparison.OrdinalIgnoreCase);

        mock.Verify(s => s.RevokeClientSecretAsync(ClientId.Create("test-client"), 1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PostRevoke_NonExistentClient_ReturnsNotFound()
    {
        HttpClient httpClient = CreateClient(MockService(), false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Secrets/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("1"), "secretId");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Secrets/non-existent?handler=Revoke")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}