using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Apis;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Apis;

/// <summary>
///     Full HTTP pipeline tests for GET /Admin/Apis, asserting on the rendered HTML via AngleSharp.
///     Each test builds its own factory/client with <see cref="IApiResourceListService" /> replaced by a
///     mock so the rendered markup is fully controlled and independent of database state.
/// </summary>
public class ApisIndexIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private HttpClient CreateClient(IApiResourceListService apiResourceListService)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => { services.AddSingleton(apiResourceListService); });
        });
        _disposables.Add(factory);

        return factory.CreateClient();
    }

    private static IApiResourceListService MockService(ListResult<ApiResourceListItem> result)
    {
        var mock = new Mock<IApiResourceListService>();
        mock.Setup(s => s.GetApiResourcesAsync(It.IsAny<ListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static ListResult<ApiResourceListItem> EmptyResult(int pageNumber = 1, int pageSize = 10) => new()
    {
        Items = Array.Empty<ApiResourceListItem>(),
        TotalCount = 0,
        PageNumber = pageNumber,
        PageSize = pageSize
    };

    private static ApiResourceListItem MakeItem(string suffix, bool enabled = true, int scopeCount = 0) => new()
    {
        Name = $"api-{suffix}",
        DisplayName = $"API {suffix}",
        Enabled = enabled,
        ScopeCount = scopeCount
    };

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    // ---------- Step 1: navigation shell & routing ----------

    [Fact]
    public async Task Get_ReturnsSuccessStatusCode()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()));

        HttpResponseMessage response = await client.GetAsync("/Admin/Apis");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_RendersAdminLayoutSidebarWithApisNavItemActive()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()));

        HttpResponseMessage response = await client.GetAsync("/Admin/Apis");
        IDocument document = await GetDocumentAsync(response);

        Assert.NotNull(document.QuerySelector("aside.sidebar"));
        IElement? navItem = document.QuerySelector("a[href*='/Admin/Apis']");
        Assert.NotNull(navItem);
        Assert.Contains("active", navItem!.ClassList);
    }

    [Fact]
    public async Task Get_RendersPageTitleAndSubtitle()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()));

        HttpResponseMessage response = await client.GetAsync("/Admin/Apis");
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal("API Resources", document.QuerySelector("h1.page-title")?.TextContent.Trim());
        Assert.Equal(
            "Secured web APIs registered with IdentityServer",
            document.QuerySelector("p.page-sub")?.TextContent.Trim());
    }

    // ---------- Step 2: tabular representation ----------

    [Fact]
    public async Task Get_RendersTableColumnHeaders()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()));

        HttpResponseMessage response = await client.GetAsync("/Admin/Apis");
        IDocument document = await GetDocumentAsync(response);

        var headers = document.QuerySelectorAll("ck-responsive-col-head").Select(h => h.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Name", "Display Name", "Scopes", "Status", "Actions" }, headers);
    }

    [Fact]
    public async Task Get_ApisExist_RendersOneRowPerApiWithMappedData()
    {
        var result = new ListResult<ApiResourceListItem>
        {
            Items = new[] { MakeItem("alpha", scopeCount: 3), MakeItem("beta", scopeCount: 1) },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 10
        };
        HttpClient client = CreateClient(MockService(result));

        HttpResponseMessage response = await client.GetAsync("/Admin/Apis");
        IDocument document = await GetDocumentAsync(response);

        IHtmlCollection<IElement> rows = document.QuerySelectorAll("ck-responsive-row");
        Assert.Equal(2, rows.Length);

        string firstRowText = rows[0].TextContent;
        Assert.Contains("API alpha", firstRowText);
        Assert.Contains("api-alpha", firstRowText);
        Assert.Contains("3", firstRowText);
    }

    [Fact]
    public async Task Get_ApiEnabled_RendersEnabledStatusBadge()
    {
        var result = new ListResult<ApiResourceListItem>
        {
            Items = new[] { MakeItem("on") },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10
        };
        HttpClient client = CreateClient(MockService(result));

        HttpResponseMessage response = await client.GetAsync("/Admin/Apis");
        IDocument document = await GetDocumentAsync(response);

        IElement? badge = document.QuerySelector(".status-badge");
        Assert.NotNull(badge);
        Assert.Contains("Enabled", badge!.TextContent);
        Assert.Contains("enabled", badge.ClassList);
    }

    [Fact]
    public async Task Get_ApiDisabled_RendersDisabledStatusBadge()
    {
        var result = new ListResult<ApiResourceListItem>
        {
            Items = new[] { MakeItem("off", false) },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10
        };
        HttpClient client = CreateClient(MockService(result));

        HttpResponseMessage response = await client.GetAsync("/Admin/Apis");
        IDocument document = await GetDocumentAsync(response);

        IElement? badge = document.QuerySelector(".status-badge");
        Assert.NotNull(badge);
        Assert.Contains("Disabled", badge!.TextContent);
        Assert.Contains("disabled", badge.ClassList);
    }

    // ---------- Step 3: filter, pagination, empty state ----------

    [Fact]
    public async Task Get_NoApisExist_RendersEmptyStateMessage()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()));

        HttpResponseMessage response = await client.GetAsync("/Admin/Apis");
        IDocument document = await GetDocumentAsync(response);

        IElement? emptyState = document.QuerySelector(".empty-state");
        Assert.NotNull(emptyState);
        Assert.Empty(document.QuerySelectorAll("ck-responsive-row"));
    }
}