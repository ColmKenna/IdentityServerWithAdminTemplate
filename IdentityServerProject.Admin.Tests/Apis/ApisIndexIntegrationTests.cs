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
using IdentityServerProject.Pages.Admin.Apis;
using IdentityServerProject.Services.Apis;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Apis;

/// <summary>
/// Full HTTP pipeline tests for GET /Admin/Apis, asserting on the rendered HTML via AngleSharp.
/// Each test builds its own factory/client with <see cref="IApiResourceListService"/> replaced by a
/// mock so the rendered markup is fully controlled and independent of database state.
/// </summary>
public class ApisIndexIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    private HttpClient CreateClient(IApiResourceListService apiResourceListService)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(apiResourceListService);
            });
        });
        _disposables.Add(factory);

        return factory.CreateClient();
    }

    private static IApiResourceListService MockService(ListResult<ApiResourceListItem> result)
    {
        var mock = new Mock<IApiResourceListService>();
        mock.Setup(s => s.GetApiResourcesAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static ListResult<ApiResourceListItem> EmptyResult(int pageNumber = 1, int pageSize = 10) => new()
    {
        Items = Array.Empty<ApiResourceListItem>(),
        TotalCount = 0,
        PageNumber = pageNumber,
        PageSize = pageSize,
    };

    private static ApiResourceListItem MakeItem(string suffix, bool enabled = true, int scopeCount = 2) => new()
    {
        Name = $"api-{suffix}",
        DisplayName = $"API Resource {suffix}",
        ScopeCount = scopeCount,
        Enabled = enabled,
    };

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    // ---------- Step 1: bare page shell & Layout ----------

    [Fact]
    public async Task Get_ReturnsSuccessStatusCode()
    {
        var client = CreateClient(MockService(EmptyResult()));

        var response = await client.GetAsync("/Admin/Apis");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_RendersAdminLayoutSidebarWithApisNavItemActive()
    {
        var client = CreateClient(MockService(EmptyResult()));

        var response = await client.GetAsync("/Admin/Apis");
        var document = await GetDocumentAsync(response);

        Assert.NotNull(document.QuerySelector("aside.sidebar"));
        var apisNavItem = document.QuerySelector("a[href*='/Admin/Apis']");
        Assert.NotNull(apisNavItem);
        Assert.Contains("active", apisNavItem!.ClassList);
    }

    [Fact]
    public async Task Get_RendersPageTitleAndSubtitle()
    {
        var client = CreateClient(MockService(EmptyResult()));

        var response = await client.GetAsync("/Admin/Apis");
        var document = await GetDocumentAsync(response);

        Assert.Equal("API Resources", document.QuerySelector("h1.page-title")?.TextContent.Trim());
        Assert.Equal(
            "Secured web APIs registered with IdentityServer",
            document.QuerySelector("p.page-sub")?.TextContent.Trim());
    }

    [Fact]
    public async Task Get_RendersNewApiResourceButtonPlaceholder()
    {
        var client = CreateClient(MockService(EmptyResult()));

        var response = await client.GetAsync("/Admin/Apis");
        var document = await GetDocumentAsync(response);

        var button = document.QuerySelector("#new-api-resource-link");
        Assert.NotNull(button);
        Assert.Contains("New API resource", button!.TextContent);
    }

    [Fact]
    public async Task Get_IncludesResponsiveTableComponentScript()
    {
        var client = CreateClient(MockService(EmptyResult()));

        var response = await client.GetAsync("/Admin/Apis");
        var document = await GetDocumentAsync(response);

        var script = document.QuerySelectorAll("script[type='module']")
            .FirstOrDefault(s => (s.GetAttribute("src") ?? "").Contains("ck-responsive-table-webcomponent"));
        Assert.NotNull(script);
    }

    // ---------- Step 2: data display ----------

    [Fact]
    public async Task Get_RendersTableColumnHeaders()
    {
        var client = CreateClient(MockService(EmptyResult()));

        var response = await client.GetAsync("/Admin/Apis");
        var document = await GetDocumentAsync(response);

        var headers = document.QuerySelectorAll("ck-responsive-col-head").Select(h => h.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Name", "Display Name", "Scopes", "Status", "Actions" }, headers);
    }

    [Fact]
    public async Task Get_ApiResourcesExist_RendersOneRowPerResourceWithMappedData()
    {
        var result = new ListResult<ApiResourceListItem>
        {
            Items = new[] { MakeItem("alpha"), MakeItem("beta") },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 10,
        };
        var client = CreateClient(MockService(result));

        var response = await client.GetAsync("/Admin/Apis");
        var document = await GetDocumentAsync(response);

        var rows = document.QuerySelectorAll("ck-responsive-row");
        Assert.Equal(2, rows.Length);

        var firstRowText = rows[0].TextContent;
        Assert.Contains("api-alpha", firstRowText);
        Assert.Contains("API Resource alpha", firstRowText);
        Assert.Contains("2 scopes", firstRowText);

        var actionLink = rows[0].QuerySelector("a.action-link");
        Assert.NotNull(actionLink);
        Assert.Contains("/Admin/Apis/Editor?name=api-alpha", actionLink!.GetAttribute("href"));
    }

    [Fact]
    public async Task Get_ApiResourceEnabled_RendersEnabledStatusBadge()
    {
        var result = new ListResult<ApiResourceListItem>
        {
            Items = new[] { MakeItem("on", enabled: true) },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10,
        };
        var client = CreateClient(MockService(result));

        var response = await client.GetAsync("/Admin/Apis");
        var document = await GetDocumentAsync(response);

        var badge = document.QuerySelector(".status-badge");
        Assert.NotNull(badge);
        Assert.Contains("Enabled", badge!.TextContent);
        Assert.Contains("enabled", badge.ClassList);
    }

    [Fact]
    public async Task Get_ApiResourceDisabled_RendersDisabledStatusBadge()
    {
        var result = new ListResult<ApiResourceListItem>
        {
            Items = new[] { MakeItem("off", enabled: false) },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10,
        };
        var client = CreateClient(MockService(result));

        var response = await client.GetAsync("/Admin/Apis");
        var document = await GetDocumentAsync(response);

        var badge = document.QuerySelector(".status-badge");
        Assert.NotNull(badge);
        Assert.Contains("Disabled", badge!.TextContent);
        Assert.Contains("disabled", badge.ClassList);
    }

    // ---------- Step 3: breadcrumbs (WI-03) ----------

    [Fact]
    public async Task Get_RendersExplicitTwoLevelBreadcrumbChain()
    {
        var client = CreateClient(MockService(EmptyResult()));

        var response = await client.GetAsync("/Admin/Apis");
        var document = await GetDocumentAsync(response);

        var crumbLinks = document.QuerySelectorAll("nav.crumbs a");
        Assert.Single(crumbLinks);
        Assert.Equal("/Admin/Apis/Index", crumbLinks[0].GetAttribute("href"));

        var current = document.QuerySelector("nav.crumbs .current");
        Assert.NotNull(current);
        Assert.Equal("API Resources", current!.TextContent.Trim());
    }

    // ---------- Step 4: pagination & filter (WI-06) ----------

    [Fact]
    public async Task Get_PagingForwardThenBack_RendersDistinctItemSetsPerPage()
    {
        var page1 = new ListResult<ApiResourceListItem>
        {
            Items = new[] { MakeItem("page1-item") },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 1,
        };
        var page2 = new ListResult<ApiResourceListItem>
        {
            Items = new[] { MakeItem("page2-item") },
            TotalCount = 2,
            PageNumber = 2,
            PageSize = 1,
        };

        var mock = new Mock<IApiResourceListService>();
        mock.Setup(s => s.GetApiResourcesAsync(null, 1, TestOptions.PageSize, It.IsAny<CancellationToken>())).ReturnsAsync(page1);
        mock.Setup(s => s.GetApiResourcesAsync(null, 2, TestOptions.PageSize, It.IsAny<CancellationToken>())).ReturnsAsync(page2);

        var client = CreateClient(mock.Object);

        var forwardResponse = await client.GetAsync("/Admin/Apis?PageNumber=2");
        var forwardDocument = await GetDocumentAsync(forwardResponse);
        Assert.Contains("api-page2-item", forwardDocument.QuerySelector("ck-responsive-row")!.TextContent);

        var backResponse = await client.GetAsync("/Admin/Apis?PageNumber=1");
        var backDocument = await GetDocumentAsync(backResponse);
        Assert.Contains("api-page1-item", backDocument.QuerySelector("ck-responsive-row")!.TextContent);
    }

    [Fact]
    public async Task Get_PageNumberBeyondLastPage_Returns200WithEmptyStateAndDisabledNext()
    {
        var beyondLastPage = new ListResult<ApiResourceListItem>
        {
            Items = Array.Empty<ApiResourceListItem>(),
            TotalCount = 1,
            PageNumber = 99,
            PageSize = 10,
        };

        var mock = new Mock<IApiResourceListService>();
        mock.Setup(s => s.GetApiResourcesAsync(null, 99, TestOptions.PageSize, It.IsAny<CancellationToken>())).ReturnsAsync(beyondLastPage);

        var client = CreateClient(mock.Object);

        var response = await client.GetAsync("/Admin/Apis?PageNumber=99");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);
        Assert.NotNull(document.QuerySelector(".empty-state"));

        var nextControl = document.QuerySelector("[data-pagination='next']");
        Assert.NotNull(nextControl);
        Assert.Contains("disabled", nextControl!.ClassList);
    }

    [Fact]
    public async Task Get_FilterSpecified_PreservedAcrossPaginationLinksAndSearchInput()
    {
        var result = new ListResult<ApiResourceListItem>
        {
            Items = new[] { MakeItem("match-1"), MakeItem("match-2") },
            TotalCount = 5,
            PageNumber = 1,
            PageSize = 2,
        };

        var mock = new Mock<IApiResourceListService>();
        mock.Setup(s => s.GetApiResourcesAsync("sales", 1, TestOptions.PageSize, It.IsAny<CancellationToken>())).ReturnsAsync(result);

        var client = CreateClient(mock.Object);

        var response = await client.GetAsync("/Admin/Apis?Filter=sales&PageNumber=1");
        var document = await GetDocumentAsync(response);

        var searchInput = document.QuerySelector("input#apis-filter") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(searchInput);
        Assert.Equal("sales", searchInput!.Value);

        var nextLink = document.QuerySelector("[data-pagination='next']");
        Assert.NotNull(nextLink);
        Assert.Contains("Filter=sales", nextLink!.GetAttribute("href"));
    }

    // ---------- Step 5: empty state ----------

    [Fact]
    public async Task Get_NoApiResourcesExist_RendersEmptyStateMessage()
    {
        var client = CreateClient(MockService(EmptyResult()));

        var response = await client.GetAsync("/Admin/Apis");
        var document = await GetDocumentAsync(response);

        var emptyState = document.QuerySelector(".empty-state");
        Assert.NotNull(emptyState);
        Assert.Empty(document.QuerySelectorAll("ck-responsive-row"));
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}
