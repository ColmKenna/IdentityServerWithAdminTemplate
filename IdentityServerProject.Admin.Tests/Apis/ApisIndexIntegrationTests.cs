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
using IdentityServerProject.Services;
using IdentityServerProject.Services.Apis;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
        mock.Setup(s => s.GetApiResourcesAsync(It.IsAny<ListQuery>(), It.IsAny<CancellationToken>()))
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

    private static ApiResourceListItem MakeItem(string suffix, bool enabled = true, int scopeCount = 0) => new()
    {
        Name = $"api-{suffix}",
        DisplayName = $"API {suffix}",
        Enabled = enabled,
        ScopeCount = scopeCount,
    };

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    // ---------- Step 1: navigation shell & routing ----------

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
        var navItem = document.QuerySelector("a[href*='/Admin/Apis']");
        Assert.NotNull(navItem);
        Assert.Contains("active", navItem!.ClassList);
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

    // ---------- Step 2: tabular representation ----------

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
    public async Task Get_ApisExist_RendersOneRowPerApiWithMappedData()
    {
        var result = new ListResult<ApiResourceListItem>
        {
            Items = new[] { MakeItem("alpha", scopeCount: 3), MakeItem("beta", scopeCount: 1) },
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
        Assert.Contains("API alpha", firstRowText);
        Assert.Contains("api-alpha", firstRowText);
        Assert.Contains("3", firstRowText);
    }

    [Fact]
    public async Task Get_ApiEnabled_RendersEnabledStatusBadge()
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
    public async Task Get_ApiDisabled_RendersDisabledStatusBadge()
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

    // ---------- Step 3: filter, pagination, empty state ----------

    [Fact]
    public async Task Get_NoApisExist_RendersEmptyStateMessage()
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
