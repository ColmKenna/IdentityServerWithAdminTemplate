using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientsSecretsIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    private HttpClient CreateClient(IClientDetailsService clientDetailsService, bool allowAutoRedirect = true)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(clientDetailsService);
            });
        });
        _disposables.Add(factory);

        return factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
    }

    private static ClientSecretsModel SampleSecrets(string id = "test-client", bool requireClientSecret = true, int secretCount = 2) => new()
    {
        ClientId = id,
        ClientName = "Co-op Market Razor Client",
        RequireClientSecret = requireClientSecret,
        Secrets = Enumerable.Range(1, secretCount)
            .Select(i => new ClientSecretSummary { Id = i, Description = $"Secret {i}", Created = DateTime.UtcNow.AddDays(-i) })
            .ToList()
    };

    private static IClientDetailsService MockService(
        ClientSecretsModel? details = null,
        ClientSecretGenerateResult? generateResult = null,
        ClientSecretRevokeResult? revokeResult = null)
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientSecretsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => id == "non-existent" ? null : (details ?? SampleSecrets(id)));
        mock.Setup(s => s.GenerateClientSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, string? _, DateTime? _, CancellationToken _) => id == "non-existent"
                ? ClientSecretGenerateResult.Failed("Client not found.")
                : (generateResult ?? ClientSecretGenerateResult.Succeeded("plaintext-secret-value")));
        mock.Setup(s => s.RevokeClientSecretAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, int _, CancellationToken _) => id == "non-existent"
                ? ClientSecretRevokeResult.Failed("Client not found.")
                : (revokeResult ?? ClientSecretRevokeResult.Succeeded()));
        return mock.Object;
    }

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    private static async Task<(string Token, string Cookie)> ExtractAntiForgeryTokenAndCookieAsync(HttpClient httpClient, string pageUrl)
    {
        var response = await httpClient.GetAsync(pageUrl);
        var document = await GetDocumentAsync(response);

        var tokenInput = document.QuerySelector("input[name='__RequestVerificationToken']") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(tokenInput);

        var token = tokenInput!.Value;

        var cookies = response.Headers.GetValues("Set-Cookie");
        var cookie = cookies.FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery"));
        Assert.NotNull(cookie);

        return (token, cookie!);
    }

    [Fact]
    public async Task Get_ExistingClient_Returns200OK_WithSecretsListedAndNoRevealBanner()
    {
        var httpClient = CreateClient(MockService());

        var response = await httpClient.GetAsync("/Admin/Clients/Secrets/test-client");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);
        Assert.Null(document.QuerySelector("#secret-reveal-banner"));
        Assert.Equal(2, document.QuerySelectorAll("[data-action='revoke-secret']").Length);
    }

    [Fact]
    public async Task Get_NonExistentClient_ReturnsNotFound()
    {
        var httpClient = CreateClient(MockService());

        var response = await httpClient.GetAsync("/Admin/Clients/Secrets/non-existent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_LastSecretOnConfidentialClient_RendersDisabledRevokeButton()
    {
        var details = SampleSecrets(requireClientSecret: true, secretCount: 1);
        var httpClient = CreateClient(MockService(details));

        var response = await httpClient.GetAsync("/Admin/Clients/Secrets/test-client");
        var document = await GetDocumentAsync(response);

        var revokeButton = document.QuerySelector("[data-action='revoke-secret']") as AngleSharp.Html.Dom.IHtmlButtonElement;
        Assert.NotNull(revokeButton);
        Assert.True(revokeButton!.IsDisabled);
    }

    [Fact]
    public async Task Get_NotLastSecret_RendersEnabledRevokeButton()
    {
        var details = SampleSecrets(requireClientSecret: true, secretCount: 2);
        var httpClient = CreateClient(MockService(details));

        var response = await httpClient.GetAsync("/Admin/Clients/Secrets/test-client");
        var document = await GetDocumentAsync(response);

        var revokeButtons = document.QuerySelectorAll("[data-action='revoke-secret']").OfType<AngleSharp.Html.Dom.IHtmlButtonElement>().ToList();
        Assert.Equal(2, revokeButtons.Count);
        Assert.All(revokeButtons, b => Assert.False(b.IsDisabled));
    }

    [Fact]
    public async Task Get_LastSecretOnPublicClient_RendersEnabledRevokeButton()
    {
        var details = SampleSecrets(requireClientSecret: false, secretCount: 1);
        var httpClient = CreateClient(MockService(details));

        var response = await httpClient.GetAsync("/Admin/Clients/Secrets/test-client");
        var document = await GetDocumentAsync(response);

        var revokeButton = document.QuerySelector("[data-action='revoke-secret']") as AngleSharp.Html.Dom.IHtmlButtonElement;
        Assert.NotNull(revokeButton);
        Assert.False(revokeButton!.IsDisabled);
    }

    [Fact]
    public async Task PostGenerate_ThenGet_ShowsRevealBannerOnce()
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientSecretsAsync("test-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSecrets());
        mock.Setup(s => s.GenerateClientSecretAsync("test-client", It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientSecretGenerateResult.Succeeded("brand-new-plaintext-secret"));

        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new System.Net.CookieContainer() };
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);
        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => services.AddSingleton(mock.Object));
        });
        _disposables.Add(factory);
        var httpClient = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = true
        });

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Secrets/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("Rotated secret"), "Description");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Secrets/test-client?handler=Generate")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        var generateResponse = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode); // auto-redirect followed

        var afterGenerateDocument = await GetDocumentAsync(generateResponse);
        var revealInput = afterGenerateDocument.QuerySelector("#generated-secret-input") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(revealInput);
        Assert.Equal("brand-new-plaintext-secret", revealInput!.Value);

        // Subsequent GET must not repeat the reveal banner (TempData is consumed).
        var secondGet = await httpClient.GetAsync("/Admin/Clients/Secrets/test-client");
        var secondDocument = await GetDocumentAsync(secondGet);
        Assert.Null(secondDocument.QuerySelector("#secret-reveal-banner"));
    }

    [Fact]
    public async Task PostGenerate_NonExistentClient_ReturnsNotFound()
    {
        var httpClient = CreateClient(MockService(), allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Secrets/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Secrets/non-existent?handler=Generate")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostGenerate_OverlongDescription_ReturnsSamePageWithFieldErrorAndDoesNotGenerate()
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientSecretsAsync("test-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSecrets());

        var httpClient = CreateClient(mock.Object, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Secrets/test-client");

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

        var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await GetDocumentAsync(response);
        var error = document.QuerySelector("[data-valmsg-for='Description']");
        Assert.NotNull(error);
        Assert.Contains(ValidationConstants.MaxClientSecretDescriptionLength.ToString(), error!.TextContent);
        mock.Verify(
            s => s.GenerateClientSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PostRevoke_Allowed_RedirectsAndRemovesSecret()
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientSecretsAsync("test-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSecrets());
        mock.Setup(s => s.RevokeClientSecretAsync("test-client", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientSecretRevokeResult.Succeeded());

        var httpClient = CreateClient(mock.Object, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Secrets/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("1"), "secretId");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Secrets/test-client?handler=Revoke")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        mock.Verify(s => s.RevokeClientSecretAsync("test-client", 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PostRevoke_BlockedLastSecret_RedirectsWithErrorAndDoesNotRemove()
    {
        var blockReason = "This is the last secret on a confidential client and cannot be revoked. Generate a replacement secret first, or disable the client's secret requirement.";
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientSecretsAsync("test-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSecrets(secretCount: 1));
        mock.Setup(s => s.RevokeClientSecretAsync("test-client", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientSecretRevokeResult.Failed(blockReason));

        var httpClient = CreateClient(mock.Object, allowAutoRedirect: true);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Secrets/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("1"), "secretId");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Secrets/test-client?handler=Revoke")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // followed redirect back to same page

        var document = await GetDocumentAsync(response);
        var errorAlert = document.QuerySelector(".alert-error");
        Assert.NotNull(errorAlert);
        Assert.Contains("last secret", errorAlert!.TextContent, StringComparison.OrdinalIgnoreCase);

        mock.Verify(s => s.RevokeClientSecretAsync("test-client", 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PostRevoke_NonExistentClient_ReturnsNotFound()
    {
        var httpClient = CreateClient(MockService(), allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Secrets/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("1"), "secretId");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Secrets/non-existent?handler=Revoke")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}
