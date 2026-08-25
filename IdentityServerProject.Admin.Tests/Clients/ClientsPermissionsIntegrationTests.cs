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

public class ClientsPermissionsIntegrationTests : IDisposable
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

    private static ClientPermissionsModel SampleInteractivePermissions(string id = "test-client") => new()
    {
        ClientId = id,
        ClientName = "Co-op Market Razor Client",
        IsInteractive = true,
        AllowedScopes = new List<string> { "openid", "profile", "coop.market.api" },
        AvailableIdentityScopes = new List<string> { "openid", "profile", "email" },
        AvailableApiScopes = new List<string> { "coop.market.api", "coop.market.admin" }
    };

    private static ClientPermissionsModel SampleM2MPermissions(string id = "test-m2m-client") => new()
    {
        ClientId = id,
        ClientName = "M2M Client",
        IsInteractive = false,
        AllowedScopes = new List<string> { "coop.market.api" },
        AvailableIdentityScopes = new List<string>(),
        AvailableApiScopes = new List<string> { "coop.market.api", "coop.market.admin" }
    };

    private static IClientDetailsService MockService(ClientPermissionsModel? details = null, bool updateSuccess = true)
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientPermissionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => id == "non-existent" ? null : (details ?? SampleInteractivePermissions(id)));
        mock.Setup(s => s.UpdateClientPermissionsAsync(It.IsAny<ClientId>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientId id, List<string> _, CancellationToken _) =>
                id.Value == "non-existent"
                    ? AdminMutationResult.NotFoundResult()
                    : updateSuccess
                        ? AdminMutationResult.Success()
                        : AdminMutationResult.ValidationFailure("Input.AllowedScopes", "Update failed."));
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
    public async Task Get_InteractiveClient_RendersOpenIdCheckedAndDisabled()
    {
        var httpClient = CreateClient(MockService(SampleInteractivePermissions()));

        var response = await httpClient.GetAsync("/Admin/Clients/Permissions/test-client");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);

        var openIdCheckbox = document.QuerySelectorAll("input[type='checkbox']")
            .OfType<AngleSharp.Html.Dom.IHtmlInputElement>()
            .FirstOrDefault(cb => cb.NextElementSibling?.TextContent.Trim() == "openid" || cb.ParentElement?.TextContent.Contains("openid") == true);
        Assert.NotNull(openIdCheckbox);
        Assert.True(openIdCheckbox!.IsChecked);
        Assert.True(openIdCheckbox.IsDisabled);

        // Disabled checkbox won't post, so a hidden input carries the enforced value.
        var hiddenOpenId = document.QuerySelector("input[type='hidden'][name='Input.AllowedScopes'][value='openid']");
        Assert.NotNull(hiddenOpenId);

        var profileCheckbox = document.QuerySelector("input[name='Input.AllowedScopes'][value='profile']") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(profileCheckbox);
        Assert.True(profileCheckbox!.IsChecked);
    }

    [Fact]
    public async Task Get_M2MClient_DoesNotRenderIdentityResourcesSection()
    {
        var httpClient = CreateClient(MockService(SampleM2MPermissions()));

        var response = await httpClient.GetAsync("/Admin/Clients/Permissions/test-m2m-client");
        var document = await GetDocumentAsync(response);

        var panelHeadings = document.QuerySelectorAll("form h2.panel-title").Select(h => h.TextContent.Trim());
        Assert.DoesNotContain("Identity Resources", panelHeadings);
        Assert.Null(document.QuerySelector("input[name='Input.AllowedScopes'][value='openid']"));

        var apiScopeCheckbox = document.QuerySelector("input[name='Input.AllowedScopes'][value='coop.market.api']") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(apiScopeCheckbox);
        Assert.True(apiScopeCheckbox!.IsChecked);
    }

    [Fact]
    public async Task Get_NonExistentClient_ReturnsNotFound()
    {
        var httpClient = CreateClient(MockService());

        var response = await httpClient.GetAsync("/Admin/Clients/Permissions/non-existent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_AddingAndRemovingApiScopes_RedirectsToDetailsAndPersists()
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientPermissionsAsync("test-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleInteractivePermissions());
        mock.Setup(s => s.UpdateClientPermissionsAsync("test-client", It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        var httpClient = CreateClient(mock.Object, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Permissions/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("openid"), "Input.AllowedScopes");
        content.Add(new StringContent("coop.market.admin"), "Input.AllowedScopes");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Permissions/test-client")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Clients/Details/test-client", response.Headers.Location?.OriginalString);

        mock.Verify(s => s.UpdateClientPermissionsAsync(
            "test-client",
            It.Is<List<string>>(l => l.Contains("coop.market.admin") && !l.Contains("coop.market.api")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_NonExistentClient_ReturnsNotFound()
    {
        var httpClient = CreateClient(MockService(), allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Permissions/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("openid"), "Input.AllowedScopes");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Permissions/non-existent")
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
