using IdentityServerProject.Admin.Tests.Infrastructure;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Pages.Admin.ApiScopes;
using IdentityServerProject.Services;
using IdentityServerProject.Services.ApiScopes;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.ApiScopes;

/// <summary>
/// Unit tests for <see cref="IndexModel"/> handler logic, exercised directly
/// against a mocked <see cref="IApiScopeListService"/> (no HTTP pipeline involved).
/// </summary>
public class ApiScopesIndexPageModelTests
{
    private static ListResult<ApiScopeListItem> MakeResult(int pageNumber = 1, int pageSize = 10) => new()
    {
        Items = new[]
        {
            new ApiScopeListItem { Name = "sales.read", DisplayName = "Read Sales Data", Enabled = true, ClientReferenceCount = 2 },
        },
        TotalCount = 1,
        PageNumber = pageNumber,
        PageSize = pageSize,
    };

    private static Pagination DefaultPagination => Pagination.From(1, TestOptions.PageSize);

    [Fact]
    public async Task OnGetAsync_NoQueryParameters_CallsServiceWithNullFilterAndFirstPage()
    {
        var mock = new Mock<IApiScopeListService>();
        mock.Setup(s => s.GetApiScopesAsync(new ListQuery(null, DefaultPagination), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult());

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole);

        await model.OnGetAsync(CancellationToken.None);

        mock.Verify(s => s.GetApiScopesAsync(new ListQuery(null, DefaultPagination), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGetAsync_ServiceReturnsResult_PopulatesApiScopesProperty()
    {
        var expected = MakeResult();
        var mock = new Mock<IApiScopeListService>();
        mock.Setup(s => s.GetApiScopesAsync(It.IsAny<ListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole);

        await model.OnGetAsync(CancellationToken.None);

        Assert.Same(expected, model.ApiScopes);
    }

    [Fact]
    public async Task OnGetAsync_FilterSet_PassesFilterToService()
    {
        var mock = new Mock<IApiScopeListService>();
        mock.Setup(s => s.GetApiScopesAsync(new ListQuery("sales", DefaultPagination), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult());
        var model = new IndexModel(mock.Object, TestOptions.AdminConsole) { Filter = "sales" };

        await model.OnGetAsync(CancellationToken.None);

        mock.Verify(s => s.GetApiScopesAsync(new ListQuery("sales", DefaultPagination), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGetAsync_PageNumberBelowOne_NormalizesToFirstPageBeforeCallingService()
    {
        var mock = new Mock<IApiScopeListService>();
        mock.Setup(s => s.GetApiScopesAsync(It.IsAny<ListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult());

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole) { PageNumber = 0 };

        await model.OnGetAsync(CancellationToken.None);

        mock.Verify(s => s.GetApiScopesAsync(new ListQuery(null, DefaultPagination), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnPostDeleteAsync_ServiceReturnsDeleted_RedirectsToPageWithoutErrorMessage()
    {
        var mock = new Mock<IApiScopeListService>();
        mock.Setup(s => s.DeleteApiScopeAsync("sales.read", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiScopeDeleteResult.Deleted);

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole);

        var result = await model.OnPostDeleteAsync("sales.read", CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(model.DeleteErrorMessage);
    }

    [Fact]
    public async Task OnPostDeleteAsync_ServiceReturnsBlocked_RedirectsWithErrorMessageAndDoesNotThrow()
    {
        var mock = new Mock<IApiScopeListService>();
        mock.Setup(s => s.DeleteApiScopeAsync("sales.read", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiScopeDeleteResult.Blocked);

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole);

        var result = await model.OnPostDeleteAsync("sales.read", CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("sales.read", model.DeleteErrorMessage);
    }

    [Fact]
    public async Task OnPostDeleteAsync_ServiceReturnsNotFound_ReturnsNotFoundResult()
    {
        var mock = new Mock<IApiScopeListService>();
        mock.Setup(s => s.DeleteApiScopeAsync("missing.scope", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiScopeDeleteResult.NotFound);

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole);

        var result = await model.OnPostDeleteAsync("missing.scope", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPostDeleteAsync_NameIsWhitespace_ReturnsNotFoundWithoutCallingService()
    {
        var mock = new Mock<IApiScopeListService>();
        var model = new IndexModel(mock.Object, TestOptions.AdminConsole);

        var result = await model.OnPostDeleteAsync("   ", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        mock.Verify(s => s.DeleteApiScopeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
