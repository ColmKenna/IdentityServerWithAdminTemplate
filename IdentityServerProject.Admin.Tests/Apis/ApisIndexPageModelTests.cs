using IdentityServerProject.Admin.Tests.Infrastructure;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Pages.Admin.Apis;
using IdentityServerProject.Services.Apis;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Apis;

/// <summary>
/// Unit tests for <see cref="IndexModel"/> handler logic, exercised directly
/// against a mocked <see cref="IApiResourceListService"/> (no HTTP pipeline involved).
/// </summary>
public class ApisIndexPageModelTests
{
    private static ListResult<ApiResourceListItem> MakeResult(int pageNumber = 1, int pageSize = 10) => new()
    {
        Items = new[]
        {
            new ApiResourceListItem { Name = "sales.api", DisplayName = "Sales API", ScopeCount = 2, Enabled = true },
        },
        TotalCount = 1,
        PageNumber = pageNumber,
        PageSize = pageSize,
    };

    [Fact]
    public async Task OnGetAsync_NoQueryParameters_CallsServiceWithNullFilterAndFirstPage()
    {
        var mock = new Mock<IApiResourceListService>();
        mock.Setup(s => s.GetApiResourcesAsync(null, 1, TestOptions.PageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult());

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole);

        await model.OnGetAsync(CancellationToken.None);

        mock.Verify(s => s.GetApiResourcesAsync(null, 1, TestOptions.PageSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGetAsync_ServiceReturnsResult_PopulatesApiResourcesProperty()
    {
        var expected = MakeResult();
        var mock = new Mock<IApiResourceListService>();
        mock.Setup(s => s.GetApiResourcesAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole);

        await model.OnGetAsync(CancellationToken.None);

        Assert.Same(expected, model.ApiResources);
    }

    [Fact]
    public async Task OnGetAsync_FilterSpecified_PassesFilterToService()
    {
        var mock = new Mock<IApiResourceListService>();
        mock.Setup(s => s.GetApiResourcesAsync("sales", 1, TestOptions.PageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult());

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole) { Filter = "sales" };

        await model.OnGetAsync(CancellationToken.None);

        mock.Verify(s => s.GetApiResourcesAsync("sales", 1, TestOptions.PageSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGetAsync_PageNumberSet_PassesPageNumberToService()
    {
        var mock = new Mock<IApiResourceListService>();
        mock.Setup(s => s.GetApiResourcesAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult(pageNumber: 3));

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole) { PageNumber = 3 };

        await model.OnGetAsync(CancellationToken.None);

        mock.Verify(s => s.GetApiResourcesAsync(null, 3, TestOptions.PageSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGetAsync_PageNumberBelowOne_NormalizesToFirstPageBeforeCallingService()
    {
        var mock = new Mock<IApiResourceListService>();
        mock.Setup(s => s.GetApiResourcesAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult());

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole) { PageNumber = 0 };

        await model.OnGetAsync(CancellationToken.None);

        mock.Verify(s => s.GetApiResourcesAsync(null, 1, TestOptions.PageSize, It.IsAny<CancellationToken>()), Times.Once);
    }
}
