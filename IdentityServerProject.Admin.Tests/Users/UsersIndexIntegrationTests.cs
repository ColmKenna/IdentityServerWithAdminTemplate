using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Users;

/// <summary>
/// Full HTTP pipeline tests for GET &amp; POST /Admin/Users, asserting on the rendered HTML via AngleSharp.
/// Each test builds its own factory/client with <see cref="IUserListService"/> replaced by a
/// mock so the rendered markup is fully controlled and independent of database state.
/// </summary>
public class UsersIndexIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    private HttpClient CreateClient(IUserListService userListService, bool allowAutoRedirect = true)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(userListService);
            });
        });
        _disposables.Add(factory);

        return factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
    }

    private static IUserListService MockService(ListResult<UserListItem> result)
    {
        var mock = new Mock<IUserListService>();
        mock.Setup(s => s.GetUsersAsync(It.IsAny<ListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static ListResult<UserListItem> EmptyResult(int pageNumber = 1, int pageSize = 10) => new()
    {
        Items = Array.Empty<UserListItem>(),
        TotalCount = 0,
        PageNumber = pageNumber,
        PageSize = pageSize,
    };

    private static UserListItem MakeItem(string suffix, bool lockedOut = false) => new()
    {
        Id = UserId.Create($"user-id-{suffix}"),
        UserName = $"username-{suffix}",
        Email = $"user-{suffix}@sales.local",
        FullName = $"Full Name {suffix}",
        IsLockedOut = lockedOut,
        LockoutEnd = lockedOut ? DateTimeOffset.UtcNow.AddDays(1) : null,
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

        var response = await client.GetAsync("/Admin/Users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_RendersAdminLayoutSidebarWithUsersNavItemActive()
    {
        var client = CreateClient(MockService(EmptyResult()));

        var response = await client.GetAsync("/Admin/Users");
        var document = await GetDocumentAsync(response);

        Assert.NotNull(document.QuerySelector("aside.sidebar"));
        var navItem = document.QuerySelector("a[href*='/Admin/Users']");
        Assert.NotNull(navItem);
        Assert.Contains("active", navItem!.ClassList);
    }

    [Fact]
    public async Task Get_RendersPageTitleAndSubtitle()
    {
        var client = CreateClient(MockService(EmptyResult()));

        var response = await client.GetAsync("/Admin/Users");
        var document = await GetDocumentAsync(response);

        Assert.Equal("Users", document.QuerySelector("h1.page-title")?.TextContent.Trim());
        Assert.Equal(
            "Provisioned user accounts and access controls",
            document.QuerySelector("p.page-sub")?.TextContent.Trim());
    }

    // ---------- Step 2: tabular representation & status display ----------

    [Fact]
    public async Task Get_RendersTableColumnHeaders()
    {
        var client = CreateClient(MockService(EmptyResult()));

        var response = await client.GetAsync("/Admin/Users");
        var document = await GetDocumentAsync(response);

        var headers = document.QuerySelectorAll("ck-responsive-col-head").Select(h => h.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "User", "Email", "Status", "Actions" }, headers);
    }

    [Fact]
    public async Task Get_UsersExist_RendersOneRowPerUserWithMappedData()
    {
        var result = new ListResult<UserListItem>
        {
            Items = new[] { MakeItem("alpha"), MakeItem("beta") },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 10,
        };
        var client = CreateClient(MockService(result));

        var response = await client.GetAsync("/Admin/Users");
        var document = await GetDocumentAsync(response);

        var rows = document.QuerySelectorAll("ck-responsive-row");
        Assert.Equal(2, rows.Length);

        var firstRowText = rows[0].TextContent;
        Assert.Contains("Full Name alpha", firstRowText);
        Assert.Contains("username-alpha", firstRowText);
        Assert.Contains("user-alpha@sales.local", firstRowText);
    }

    [Fact]
    public async Task Get_UserLockedOut_RendersLockedOutBadgeAndUnlockButton()
    {
        var result = new ListResult<UserListItem>
        {
            Items = new[] { MakeItem("locked", lockedOut: true) },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10,
        };
        var client = CreateClient(MockService(result));

        var response = await client.GetAsync("/Admin/Users");
        var document = await GetDocumentAsync(response);

        var badge = document.QuerySelector(".status-badge");
        Assert.NotNull(badge);
        Assert.Contains("Locked Out", badge!.TextContent);
        Assert.Contains("disabled", badge.ClassList);

        var unlockBtn = document.QuerySelector("button.unlock-btn");
        Assert.NotNull(unlockBtn);
    }

    [Fact]
    public async Task Get_UserActive_RendersActiveBadgeWithoutUnlockButton()
    {
        var result = new ListResult<UserListItem>
        {
            Items = new[] { MakeItem("active", lockedOut: false) },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10,
        };
        var client = CreateClient(MockService(result));

        var response = await client.GetAsync("/Admin/Users");
        var document = await GetDocumentAsync(response);

        var badge = document.QuerySelector(".status-badge");
        Assert.NotNull(badge);
        Assert.Contains("Active", badge!.TextContent);
        Assert.Contains("enabled", badge.ClassList);

        var unlockBtn = document.QuerySelector("button.unlock-btn");
        Assert.Null(unlockBtn);
    }

    // ---------- Step 3: pagination & filter (WI-06) ----------

    [Fact]
    public async Task Get_PagingForwardThenBack_RendersDistinctItemSetsPerPage()
    {
        var page1 = new ListResult<UserListItem>
        {
            Items = new[] { MakeItem("page1-item") },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 1,
        };
        var page2 = new ListResult<UserListItem>
        {
            Items = new[] { MakeItem("page2-item") },
            TotalCount = 2,
            PageNumber = 2,
            PageSize = 1,
        };

        var mock = new Mock<IUserListService>();
        mock.Setup(s => s.GetUsersAsync(It.Is<ListQuery>(q => q.Filter == null && q.Pagination == Pagination.From(1, TestOptions.PageSize)), It.IsAny<CancellationToken>())).ReturnsAsync(page1);
        mock.Setup(s => s.GetUsersAsync(It.Is<ListQuery>(q => q.Filter == null && q.Pagination == Pagination.From(2, TestOptions.PageSize)), It.IsAny<CancellationToken>())).ReturnsAsync(page2);

        var client = CreateClient(mock.Object);

        var forwardResponse = await client.GetAsync("/Admin/Users?PageNumber=2");
        var forwardDocument = await GetDocumentAsync(forwardResponse);
        Assert.Contains("username-page2-item", forwardDocument.QuerySelector("ck-responsive-row")!.TextContent);

        var backResponse = await client.GetAsync("/Admin/Users?PageNumber=1");
        var backDocument = await GetDocumentAsync(backResponse);
        Assert.Contains("username-page1-item", backDocument.QuerySelector("ck-responsive-row")!.TextContent);
    }

    [Fact]
    public async Task Get_PageNumberBeyondLastPage_Returns200WithEmptyStateAndDisabledNext()
    {
        var beyondLastPage = new ListResult<UserListItem>
        {
            Items = Array.Empty<UserListItem>(),
            TotalCount = 1,
            PageNumber = 99,
            PageSize = 10,
        };

        var mock = new Mock<IUserListService>();
        mock.Setup(s => s.GetUsersAsync(It.Is<ListQuery>(q => q.Filter == null && q.Pagination == Pagination.From(99, TestOptions.PageSize)), It.IsAny<CancellationToken>())).ReturnsAsync(beyondLastPage);

        var client = CreateClient(mock.Object);

        var response = await client.GetAsync("/Admin/Users?PageNumber=99");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);
        Assert.NotNull(document.QuerySelector(".empty-state"));

        var nextControl = document.QuerySelector("[data-pagination='next']");
        Assert.NotNull(nextControl);
        Assert.Contains("disabled", nextControl!.ClassList);
    }

    [Fact]
    public async Task Get_FilterSpecified_PreservedAcrossPaginationLinks()
    {
        var result = new ListResult<UserListItem>
        {
            Items = new[] { MakeItem("match-1"), MakeItem("match-2") },
            TotalCount = 5,
            PageNumber = 1,
            PageSize = 2,
        };

        var mock = new Mock<IUserListService>();
        mock.Setup(s => s.GetUsersAsync(It.Is<ListQuery>(q => q.Filter == "admin" && q.Pagination == Pagination.From(1, TestOptions.PageSize)), It.IsAny<CancellationToken>())).ReturnsAsync(result);

        var client = CreateClient(mock.Object);

        var response = await client.GetAsync("/Admin/Users?Filter=admin&PageNumber=1");
        var document = await GetDocumentAsync(response);

        var nextLink = document.QuerySelector("[data-pagination='next']");
        Assert.NotNull(nextLink);
        Assert.Contains("Filter=admin", nextLink!.GetAttribute("href"));
    }

    // ---------- Step 4: post handler (unlock) & empty state ----------

    [Fact]
    public async Task Get_NoUsersExist_RendersEmptyStateMessage()
    {
        var client = CreateClient(MockService(EmptyResult()));

        var response = await client.GetAsync("/Admin/Users");
        var document = await GetDocumentAsync(response);

        var emptyState = document.QuerySelector(".empty-state");
        Assert.NotNull(emptyState);
        Assert.Empty(document.QuerySelectorAll("ck-responsive-row"));
    }

    [Fact]
    public async Task PostUnlock_ValidUser_UnlocksAndRedirectsToUsersList()
    {
        var mockService = new Mock<IUserListService>();
        var result = new ListResult<UserListItem>
        {
            Items = new[] { MakeItem("locked", lockedOut: true) },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10,
        };
        mockService.Setup(s => s.GetUsersAsync(It.IsAny<ListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        mockService.Setup(s => s.UnlockUserAsync(UserId.Create("user-id-locked"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserUnlockResult.Succeeded);

        var client = CreateClient(mockService.Object, allowAutoRedirect: false);

        var getResponse = await client.GetAsync("/Admin/Users");
        var getDocument = await GetDocumentAsync(getResponse);

        var tokenInput = getDocument.QuerySelector("input[name='__RequestVerificationToken']") as IHtmlInputElement;
        Assert.NotNull(tokenInput);

        var token = tokenInput!.Value;
        var cookies = getResponse.Headers.GetValues("Set-Cookie");
        var cookie = cookies.FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery"));
        Assert.NotNull(cookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users?handler=Unlock&id=user-id-locked");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Admin/Users", response.Headers.Location?.OriginalString);
        mockService.Verify(s => s.UnlockUserAsync(UserId.Create("user-id-locked"), It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}
