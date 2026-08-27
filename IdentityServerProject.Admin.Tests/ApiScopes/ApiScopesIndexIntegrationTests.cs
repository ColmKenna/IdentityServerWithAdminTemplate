using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.ApiScopes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.ApiScopes;

/// <summary>
///     Full HTTP pipeline tests for GET /Admin/ApiScopes, asserting on the rendered HTML via AngleSharp.
///     Each test builds its own factory/client with <see cref="IApiScopeListService" /> replaced by a
///     mock so the rendered markup is fully controlled and independent of database state.
/// </summary>
public class ApiScopesIndexIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private HttpClient CreateClient(IApiScopeListService apiScopeListService, bool allowAutoRedirect = true)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => { services.AddSingleton(apiScopeListService); });
        });
        _disposables.Add(factory);

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
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

    private static Mock<IApiScopeListService> MockService(ListResult<ApiScopeListItem> result)
    {
        var mock = new Mock<IApiScopeListService>();
        mock.Setup(s => s.GetApiScopesAsync(It.IsAny<ListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    private static ListResult<ApiScopeListItem> EmptyResult(int pageNumber = 1, int pageSize = 10) => new()
    {
        Items = Array.Empty<ApiScopeListItem>(),
        TotalCount = 0,
        PageNumber = pageNumber,
        PageSize = pageSize
    };

    private static ApiScopeListItem MakeItem(string suffix, bool enabled = true, int referenceCount = 0) => new()
    {
        Name = $"scope-{suffix}",
        DisplayName = $"Scope {suffix}",
        Enabled = enabled,
        ClientReferenceCount = referenceCount
    };

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    // ---------- bare page shell & Layout ----------

    [Fact]
    public async Task Get_ReturnsSuccessStatusCode()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()).Object);

        HttpResponseMessage response = await client.GetAsync("/Admin/ApiScopes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_RendersAdminLayoutSidebarWithApiScopesNavItemActive()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()).Object);

        HttpResponseMessage response = await client.GetAsync("/Admin/ApiScopes");
        IDocument document = await GetDocumentAsync(response);

        Assert.NotNull(document.QuerySelector("aside.sidebar"));
        IElement? navItem = document.QuerySelector("a[href*='/Admin/ApiScopes']");
        Assert.NotNull(navItem);
        Assert.Contains("active", navItem!.ClassList);
    }

    [Fact]
    public async Task Get_RendersPageTitleAndSubtitle()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()).Object);

        HttpResponseMessage response = await client.GetAsync("/Admin/ApiScopes");
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal("API Scopes", document.QuerySelector("h1.page-title")?.TextContent.Trim());
        Assert.Equal(
            "Access-control scopes clients can request from IdentityServer",
            document.QuerySelector("p.page-sub")?.TextContent.Trim());
    }

    [Fact]
    public async Task Get_RendersTableColumnHeaders()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()).Object);

        HttpResponseMessage response = await client.GetAsync("/Admin/ApiScopes");
        IDocument document = await GetDocumentAsync(response);

        var headers = document.QuerySelectorAll("ck-responsive-col-head").Select(h => h.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Name", "Display Name", "Referenced By", "Status", "Actions" }, headers);
    }

    [Fact]
    public async Task Get_ApiScopesExist_RendersOneRowPerScopeWithMappedData()
    {
        var result = new ListResult<ApiScopeListItem>
        {
            Items = new[] { MakeItem("alpha", referenceCount: 3), MakeItem("beta") },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 10
        };
        HttpClient client = CreateClient(MockService(result).Object);

        HttpResponseMessage response = await client.GetAsync("/Admin/ApiScopes");
        IDocument document = await GetDocumentAsync(response);

        IHtmlCollection<IElement> rows = document.QuerySelectorAll("ck-responsive-row");
        Assert.Equal(2, rows.Length);

        string firstRowText = rows[0].TextContent;
        Assert.Contains("scope-alpha", firstRowText);
        Assert.Contains("Scope alpha", firstRowText);
        Assert.Contains("3 clients", firstRowText);
    }

    [Fact]
    public async Task Get_ApiScopeEnabled_RendersEnabledStatusBadge()
    {
        var result = new ListResult<ApiScopeListItem>
        {
            Items = new[] { MakeItem("on") },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10
        };
        HttpClient client = CreateClient(MockService(result).Object);

        HttpResponseMessage response = await client.GetAsync("/Admin/ApiScopes");
        IDocument document = await GetDocumentAsync(response);

        IElement? badge = document.QuerySelector(".status-badge");
        Assert.NotNull(badge);
        Assert.Contains("Enabled", badge!.TextContent);
        Assert.Contains("enabled", badge.ClassList);
    }

    [Fact]
    public async Task Get_ApiScopeDisabled_RendersDisabledStatusBadge()
    {
        var result = new ListResult<ApiScopeListItem>
        {
            Items = new[] { MakeItem("off", false) },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10
        };
        HttpClient client = CreateClient(MockService(result).Object);

        HttpResponseMessage response = await client.GetAsync("/Admin/ApiScopes");
        IDocument document = await GetDocumentAsync(response);

        IElement? badge = document.QuerySelector(".status-badge");
        Assert.NotNull(badge);
        Assert.Contains("Disabled", badge!.TextContent);
        Assert.Contains("disabled", badge.ClassList);
    }

    [Fact]
    public async Task Get_NoApiScopesExist_RendersEmptyStateMessage()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()).Object);

        HttpResponseMessage response = await client.GetAsync("/Admin/ApiScopes");
        IDocument document = await GetDocumentAsync(response);

        IElement? emptyState = document.QuerySelector(".empty-state");
        Assert.NotNull(emptyState);
        Assert.Empty(document.QuerySelectorAll("ck-responsive-row"));
    }

    // ---------- delete modal wiring ----------

    [Fact]
    public async Task Get_RendersDeleteModalDialog()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()).Object);

        HttpResponseMessage response = await client.GetAsync("/Admin/ApiScopes");
        IDocument document = await GetDocumentAsync(response);

        IElement? dialog = document.QuerySelector("dialog#delete-scope-modal");
        Assert.NotNull(dialog);
        Assert.NotNull(dialog!.QuerySelector("form[method='post']"));
    }

    [Fact]
    public async Task Get_ApiScopesExist_RendersDeleteButtonWithReferenceCountDataAttribute()
    {
        var result = new ListResult<ApiScopeListItem>
        {
            Items = new[] { MakeItem("referenced", referenceCount: 4) },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10
        };
        HttpClient client = CreateClient(MockService(result).Object);

        HttpResponseMessage response = await client.GetAsync("/Admin/ApiScopes");
        IDocument document = await GetDocumentAsync(response);

        IElement? deleteButton = document.QuerySelector("[data-action='delete-scope']");
        Assert.NotNull(deleteButton);
        Assert.Equal("scope-referenced", deleteButton!.GetAttribute("data-delete-name"));
        Assert.Equal("4", deleteButton.GetAttribute("data-reference-count"));
    }

    // ---------- delete handler ----------

    [Fact]
    public async Task PostDelete_ScopeUnreferenced_DeletesAndRedirectsToIndex()
    {
        Mock<IApiScopeListService> mock = MockService(EmptyResult());
        mock.Setup(s => s.DeleteApiScopeAsync("scope-alpha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiScopeDeleteResult.Deleted);
        HttpClient client = CreateClient(mock.Object, false);

        (string token, string cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/ApiScopes");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/ApiScopes?handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = "scope-alpha",
            ["PageNumber"] = "1"
        });

        HttpResponseMessage response = await client.SendAsync(request);

        mock.Verify(s => s.DeleteApiScopeAsync("scope-alpha", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task PostDelete_ScopeBlocked_RedirectsWithoutDeletingAndSurfacesErrorMessage()
    {
        Mock<IApiScopeListService> mock = MockService(EmptyResult());
        mock.Setup(s => s.DeleteApiScopeAsync("scope-inuse", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiScopeDeleteResult.Blocked);
        HttpClient client = CreateClient(mock.Object, false);

        (string token, string cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/ApiScopes");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/ApiScopes?handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = "scope-inuse",
            ["PageNumber"] = "1"
        });

        HttpResponseMessage response = await client.SendAsync(request);

        mock.Verify(s => s.DeleteApiScopeAsync("scope-inuse", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var followUpRequest = new HttpRequestMessage(HttpMethod.Get, response.Headers.Location);
        followUpRequest.Headers.Add("Cookie", cookie);
        HttpResponseMessage redirected = await client.SendAsync(followUpRequest);
        IDocument document = await GetDocumentAsync(redirected);
        IElement? alert = document.QuerySelector(".alert-error");
        Assert.NotNull(alert);
        Assert.Contains("scope-inuse", alert!.TextContent);
    }
}