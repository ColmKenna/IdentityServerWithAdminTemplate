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

public class ClientsTokenSettingsIntegrationTests : IDisposable
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

    private static ClientTokenSettingsModel SampleSettings(string id = "test-client") => new()
    {
        ClientId = id,
        ClientName = "Co-op Market Razor Client",
        AccessTokenLifetime = 3600,
        IdentityTokenLifetime = 300,
        RequireConsent = false,
        AllowOfflineAccess = true
    };

    private static IClientDetailsService MockService(ClientTokenSettingsModel? details = null, bool updateSuccess = true)
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientTokenSettingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => id == "non-existent" ? null : (details ?? SampleSettings(id)));
        mock.Setup(s => s.UpdateClientTokenSettingsAsync(It.IsAny<string>(), It.IsAny<ClientTokenSettingsInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, ClientTokenSettingsInputModel _, CancellationToken _) =>
                id == "non-existent"
                    ? AdminMutationResult.NotFoundResult()
                    : updateSuccess
                        ? AdminMutationResult.Success()
                        : AdminMutationResult.ValidationFailure("Input", "Update failed."));
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
    public async Task Get_ExistingClient_Returns200OK_WithCurrentSettings()
    {
        var httpClient = CreateClient(MockService());

        var response = await httpClient.GetAsync("/Admin/Clients/TokenSettings/test-client");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);

        var accessTokenInput = document.QuerySelector("input[name='Input.AccessTokenLifetime']") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(accessTokenInput);
        Assert.Equal("3600", accessTokenInput!.Value);

        var identityTokenInput = document.QuerySelector("input[name='Input.IdentityTokenLifetime']") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(identityTokenInput);
        Assert.Equal("300", identityTokenInput!.Value);

        var offlineCheckbox = document.QuerySelector("input[name='Input.AllowOfflineAccess']") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(offlineCheckbox);
        Assert.True(offlineCheckbox!.IsChecked);
    }

    [Fact]
    public async Task Get_NonExistentClient_ReturnsNotFound()
    {
        var httpClient = CreateClient(MockService());

        var response = await httpClient.GetAsync("/Admin/Clients/TokenSettings/non-existent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ValidValues_RedirectsToDetailsAndPersists()
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientTokenSettingsAsync("test-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSettings());
        mock.Setup(s => s.UpdateClientTokenSettingsAsync("test-client", It.IsAny<ClientTokenSettingsInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        var httpClient = CreateClient(mock.Object, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/TokenSettings/test-client");

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

        var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Clients/Details/test-client", response.Headers.Location?.OriginalString);

        mock.Verify(s => s.UpdateClientTokenSettingsAsync(
            "test-client",
            It.Is<ClientTokenSettingsInputModel>(m => m.AccessTokenLifetime == 7200
                && m.IdentityTokenLifetime == 600
                && m.RequireConsent
                && m.AllowOfflineAccess
                && m.RefreshTokenUsage == 1
                && m.RefreshTokenExpiration == 1
                && m.AbsoluteRefreshTokenLifetime == 172800
                && m.SlidingRefreshTokenLifetime == 72000),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_AccessTokenLifetimeOutOfRange_ReturnsValidationErrorAndDoesNotPersist()
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientTokenSettingsAsync("test-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSettings());

        var httpClient = CreateClient(mock.Object, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/TokenSettings/test-client");

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

        var response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);
        var summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);

        mock.Verify(s => s.UpdateClientTokenSettingsAsync(It.IsAny<string>(), It.IsAny<ClientTokenSettingsInputModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Post_IdentityTokenLifetimeOutOfRange_ReturnsValidationErrorAndDoesNotPersist()
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientTokenSettingsAsync("test-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSettings());

        var httpClient = CreateClient(mock.Object, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/TokenSettings/test-client");

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

        var response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);
        var summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);

        mock.Verify(s => s.UpdateClientTokenSettingsAsync(It.IsAny<string>(), It.IsAny<ClientTokenSettingsInputModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Post_IdentityTokenLifetimeOutOfRange_ReturnsValidationErrorWithLifetimesTabActive()
    {
        var httpClient = CreateClient(MockService(), allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/TokenSettings/test-client");

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

        var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await GetDocumentAsync(response);
        var activeTab = document.QuerySelector("#client-token-settings-tabs > ck-tab[active]");
        Assert.NotNull(activeTab);
        Assert.Equal("Token Lifetimes", activeTab!.GetAttribute("label"));
    }

    [Fact]
    public async Task Post_NonExistentClient_ReturnsNotFound()
    {
        var httpClient = CreateClient(MockService(), allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/TokenSettings/test-client");

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
