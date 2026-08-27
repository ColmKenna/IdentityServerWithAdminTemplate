using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientsPermissionsIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private HttpClient CreateClient(IClientDetailsService clientDetailsService, bool allowAutoRedirect = true)
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

    private static ClientPermissionsModel SampleInteractivePermissions(string id = "test-client") => new()
    {
        ClientId = ClientId.Create(id),
        ClientName = "Co-op Market Razor Client",
        IsInteractive = true,
        AllowedScopes = new List<string> { "openid", "profile", "coop.market.api" },
        AvailableIdentityScopes = new List<string> { "openid", "profile", "email" },
        AvailableApiScopes = new List<string> { "coop.market.api", "coop.market.admin" }
    };

    private static ClientPermissionsModel SampleM2MPermissions(string id = "test-m2m-client") => new()
    {
        ClientId = ClientId.Create(id),
        ClientName = "M2M Client",
        IsInteractive = false,
        AllowedScopes = new List<string> { "coop.market.api" },
        AvailableIdentityScopes = new List<string>(),
        AvailableApiScopes = new List<string> { "coop.market.api", "coop.market.admin" }
    };

    private static IClientDetailsService MockService(ClientPermissionsModel? details = null, bool updateSuccess = true)
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientPermissionsAsync(It.IsAny<ClientId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientId id, CancellationToken _) =>
                id.Value == "non-existent" ? null : details ?? SampleInteractivePermissions(id.Value));
        mock.Setup(s =>
                s.UpdateClientPermissionsAsync(It.IsAny<ClientId>(), It.IsAny<ScopeSet>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientId id, ScopeSet _, CancellationToken _) =>
                id.Value == "non-existent"
                    ? AdminMutationResult.NotFoundResult()
                    : updateSuccess
                        ? AdminMutationResult.Success()
                        : AdminMutationResult.ValidationFailure("Input.AllowedScopes", "Update failed."));
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
    public async Task Get_InteractiveClient_RendersOpenIdCheckedAndDisabled()
    {
        HttpClient httpClient = CreateClient(MockService(SampleInteractivePermissions()));

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Clients/Permissions/test-client");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);

        IHtmlInputElement? openIdCheckbox = document.QuerySelectorAll("input[type='checkbox']")
            .OfType<IHtmlInputElement>()
            .FirstOrDefault(cb =>
                cb.NextElementSibling?.TextContent.Trim() == "openid" ||
                cb.ParentElement?.TextContent.Contains("openid") == true);
        Assert.NotNull(openIdCheckbox);
        Assert.True(openIdCheckbox!.IsChecked);
        Assert.True(openIdCheckbox.IsDisabled);

        // Disabled checkbox won't post, so a hidden input carries the enforced value.
        IElement? hiddenOpenId =
            document.QuerySelector("input[type='hidden'][name='Input.AllowedScopes'][value='openid']");
        Assert.NotNull(hiddenOpenId);

        var profileCheckbox =
            document.QuerySelector("input[name='Input.AllowedScopes'][value='profile']") as IHtmlInputElement;
        Assert.NotNull(profileCheckbox);
        Assert.True(profileCheckbox!.IsChecked);
    }

    [Fact]
    public async Task Get_M2MClient_DoesNotRenderIdentityResourcesSection()
    {
        HttpClient httpClient = CreateClient(MockService(SampleM2MPermissions()));

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Clients/Permissions/test-m2m-client");
        IDocument document = await GetDocumentAsync(response);

        IEnumerable<string> panelHeadings =
            document.QuerySelectorAll("form h2.panel-title").Select(h => h.TextContent.Trim());
        Assert.DoesNotContain("Identity Resources", panelHeadings);
        Assert.Null(document.QuerySelector("input[name='Input.AllowedScopes'][value='openid']"));

        var apiScopeCheckbox =
            document.QuerySelector("input[name='Input.AllowedScopes'][value='coop.market.api']") as IHtmlInputElement;
        Assert.NotNull(apiScopeCheckbox);
        Assert.True(apiScopeCheckbox!.IsChecked);
    }

    [Fact]
    public async Task Get_NonExistentClient_ReturnsNotFound()
    {
        HttpClient httpClient = CreateClient(MockService());

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Clients/Permissions/non-existent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_AddingAndRemovingApiScopes_RedirectsToDetailsAndPersists()
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientPermissionsAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleInteractivePermissions());
        mock.Setup(s =>
                s.UpdateClientPermissionsAsync(ClientId.Create("test-client"), It.IsAny<ScopeSet>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Permissions/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("openid"), "Input.AllowedScopes");
        content.Add(new StringContent("coop.market.admin"), "Input.AllowedScopes");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Permissions/test-client")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Clients/Details/test-client", response.Headers.Location?.OriginalString);

        mock.Verify(s => s.UpdateClientPermissionsAsync(
            ClientId.Create("test-client"),
            It.Is<ScopeSet>(scopes =>
                scopes.Contains(ScopeName.Create("coop.market.admin")) &&
                !scopes.Contains(ScopeName.Create("coop.market.api"))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_NonExistentClient_ReturnsNotFound()
    {
        HttpClient httpClient = CreateClient(MockService(), false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Permissions/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("openid"), "Input.AllowedScopes");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Permissions/non-existent")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}