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
using IdentityServerProject.Services.ApiScopes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.ApiScopes;

/// <summary>
/// Full HTTP pipeline tests for GET /Admin/ApiScopes, asserting on the rendered HTML via AngleSharp.
/// Each test builds its own factory/client with <see cref="IApiScopeListService"/> replaced by a
/// mock so the rendered markup is fully controlled and independent of database state.
/// </summary>
public class ApiScopesIndexIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    private HttpClient CreateClient(IApiScopeListService apiScopeListService, bool allowAutoRedirect = true)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(apiScopeListService);
            });
        });
        _disposables.Add(factory);

        return factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
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
        PageSize = pageSize,
    };

    private static ApiScopeListItem MakeItem(string suffix, bool enabled = true, int referenceCount = 0) => new()
    {
        Name = $"scope-{suffix}",
        DisplayName = $"Scope {suffix}",
        Enabled = enabled,
        ClientReferenceCount = referenceCount,
    };

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    // ---------- bare page shell & Layout ----------

    [Fact]
    public async Task Get_ReturnsSuccessStatusCode()
    {
        var client = CreateClient(MockService(EmptyResult()).Object);

        var response = await client.GetAsync("/Admin/ApiScopes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_RendersAdminLayoutSidebarWithApiScopesNavItemActive()
    {
        var client = CreateClient(MockService(EmptyResult()).Object);

        var response = await client.GetAsync("/Admin/ApiScopes");
        var document = await GetDocumentAsync(response);

        Assert.NotNull(document.QuerySelector("aside.sidebar"));
        var navItem = document.QuerySelector("a[href*='/Admin/ApiScopes']");
        Assert.NotNull(navItem);
        Assert.Contains("active", navItem!.ClassList);
    }

    [Fact]
    public async Task Get_RendersPageTitleAndSubtitle()
    {
        var client = CreateClient(MockService(EmptyResult()).Object);

        var response = await client.GetAsync("/Admin/ApiScopes");
        var document = await GetDocumentAsync(response);

        Assert.Equal("API Scopes", document.QuerySelector("h1.page-title")?.TextContent.Trim());
        Assert.Equal(
            "Access-control scopes clients can request from IdentityServer",
            document.QuerySelector("p.page-sub")?.TextContent.Trim());
    }

    [Fact]
    public async Task Get_RendersTableColumnHeaders()
    {
        var client = CreateClient(MockService(EmptyResult()).Object);

        var response = await client.GetAsync("/Admin/ApiScopes");
        var document = await GetDocumentAsync(response);

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
            PageSize = 10,
        };
        var client = CreateClient(MockService(result).Object);

        var response = await client.GetAsync("/Admin/ApiScopes");
        var document = await GetDocumentAsync(response);

        var rows = document.QuerySelectorAll("ck-responsive-row");
        Assert.Equal(2, rows.Length);

        var firstRowText = rows[0].TextContent;
        Assert.Contains("scope-alpha", firstRowText);
        Assert.Contains("Scope alpha", firstRowText);
        Assert.Contains("3 clients", firstRowText);
    }

    [Fact]
    public async Task Get_ApiScopeEnabled_RendersEnabledStatusBadge()
    {
        var result = new ListResult<ApiScopeListItem>
        {
            Items = new[] { MakeItem("on", enabled: true) },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10,
        };
        var client = CreateClient(MockService(result).Object);

        var response = await client.GetAsync("/Admin/ApiScopes");
        var document = await GetDocumentAsync(response);

        var badge = document.QuerySelector(".status-badge");
        Assert.NotNull(badge);
        Assert.Contains("Enabled", badge!.TextContent);
        Assert.Contains("enabled", badge.ClassList);
    }

    [Fact]
    public async Task Get_ApiScopeDisabled_RendersDisabledStatusBadge()
    {
        var result = new ListResult<ApiScopeListItem>
        {
            Items = new[] { MakeItem("off", enabled: false) },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10,
        };
        var client = CreateClient(MockService(result).Object);

        var response = await client.GetAsync("/Admin/ApiScopes");
        var document = await GetDocumentAsync(response);

        var badge = document.QuerySelector(".status-badge");
        Assert.NotNull(badge);
        Assert.Contains("Disabled", badge!.TextContent);
        Assert.Contains("disabled", badge.ClassList);
    }

    [Fact]
    public async Task Get_NoApiScopesExist_RendersEmptyStateMessage()
    {
        var client = CreateClient(MockService(EmptyResult()).Object);

        var response = await client.GetAsync("/Admin/ApiScopes");
        var document = await GetDocumentAsync(response);

        var emptyState = document.QuerySelector(".empty-state");
        Assert.NotNull(emptyState);
        Assert.Empty(document.QuerySelectorAll("ck-responsive-row"));
    }

    // ---------- delete modal wiring ----------

    [Fact]
    public async Task Get_RendersDeleteModalDialog()
    {
        var client = CreateClient(MockService(EmptyResult()).Object);

        var response = await client.GetAsync("/Admin/ApiScopes");
        var document = await GetDocumentAsync(response);

        var dialog = document.QuerySelector("dialog#delete-scope-modal");
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
            PageSize = 10,
        };
        var client = CreateClient(MockService(result).Object);

        var response = await client.GetAsync("/Admin/ApiScopes");
        var document = await GetDocumentAsync(response);

        var deleteButton = document.QuerySelector("[data-action='delete-scope']");
        Assert.NotNull(deleteButton);
        Assert.Equal("scope-referenced", deleteButton!.GetAttribute("data-delete-name"));
        Assert.Equal("4", deleteButton.GetAttribute("data-reference-count"));
    }

    // ---------- delete handler ----------

    [Fact]
    public async Task PostDelete_ScopeUnreferenced_DeletesAndRedirectsToIndex()
    {
        var mock = MockService(EmptyResult());
        mock.Setup(s => s.DeleteApiScopeAsync("scope-alpha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiScopeDeleteResult.Deleted);
        var client = CreateClient(mock.Object, allowAutoRedirect: false);

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/ApiScopes");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/ApiScopes?handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = "scope-alpha",
            ["PageNumber"] = "1",
        });

        var response = await client.SendAsync(request);

        mock.Verify(s => s.DeleteApiScopeAsync("scope-alpha", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task PostDelete_ScopeBlocked_RedirectsWithoutDeletingAndSurfacesErrorMessage()
    {
        var mock = MockService(EmptyResult());
        mock.Setup(s => s.DeleteApiScopeAsync("scope-inuse", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiScopeDeleteResult.Blocked);
        var client = CreateClient(mock.Object, allowAutoRedirect: false);

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/ApiScopes");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/ApiScopes?handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = "scope-inuse",
            ["PageNumber"] = "1",
        });

        var response = await client.SendAsync(request);

        mock.Verify(s => s.DeleteApiScopeAsync("scope-inuse", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var followUpRequest = new HttpRequestMessage(HttpMethod.Get, response.Headers.Location);
        followUpRequest.Headers.Add("Cookie", cookie);
        var redirected = await client.SendAsync(followUpRequest);
        var document = await GetDocumentAsync(redirected);
        var alert = document.QuerySelector(".alert-error");
        Assert.NotNull(alert);
        Assert.Contains("scope-inuse", alert!.TextContent);
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}
