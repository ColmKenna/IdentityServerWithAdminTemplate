using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Pages.Admin.Clients;
using IdentityServerProject.Services.Clients;
using Moq;

namespace IdentityServerProject.Admin.Tests.Clients;

/// <summary>
///     Unit tests for <see cref="IndexModel" /> handler logic, exercised directly
///     against a mocked <see cref="IClientListService" /> (no HTTP pipeline involved).
/// </summary>
public class ClientsIndexPageModelTests
{
    private static Pagination DefaultPagination => Pagination.From(1, TestOptions.PageSize);

    private static ListResult<ClientListItem> MakeResult(int pageNumber = 1, int pageSize = 10) => new()
    {
        Items = new[]
        {
            new ClientListItem
                { ClientId = "c1", ClientName = "Client One", ClientType = "Authorization Code", Enabled = true }
        },
        TotalCount = 1,
        PageNumber = pageNumber,
        PageSize = pageSize
    };

    [Fact]
    public async Task OnGetAsync_NoQueryParameters_CallsServiceWithNullFilterAndFirstPage()
    {
        var mock = new Mock<IClientListService>();
        mock.Setup(s => s.GetClientsAsync(It.Is<ListQuery>(q => q.Filter == null && q.Pagination == DefaultPagination),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult());

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole);

        await model.OnGetAsync(CancellationToken.None);

        mock.Verify(
            s => s.GetClientsAsync(It.Is<ListQuery>(q => q.Filter == null && q.Pagination == DefaultPagination),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGetAsync_ServiceReturnsResult_PopulatesClientsProperty()
    {
        ListResult<ClientListItem> expected = MakeResult();
        var mock = new Mock<IClientListService>();
        mock.Setup(s => s.GetClientsAsync(It.IsAny<ListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole);

        await model.OnGetAsync(CancellationToken.None);

        Assert.Same(expected, model.Clients);
    }

    [Fact]
    public async Task OnGetAsync_FilterSet_PassesFilterToService()
    {
        var mock = new Mock<IClientListService>();
        mock.Setup(s =>
                s.GetClientsAsync(It.Is<ListQuery>(q => q.Filter == "portal" && q.Pagination == DefaultPagination),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult());
        var model = new IndexModel(mock.Object, TestOptions.AdminConsole) { Filter = "portal" };

        await model.OnGetAsync(CancellationToken.None);

        mock.Verify(
            s => s.GetClientsAsync(It.Is<ListQuery>(q => q.Filter == "portal" && q.Pagination == DefaultPagination),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGetAsync_PageNumberSet_PassesPageNumberToService()
    {
        var page3 = Pagination.From(3, TestOptions.PageSize);
        var mock = new Mock<IClientListService>();
        mock.Setup(s => s.GetClientsAsync(It.Is<ListQuery>(q => q.Pagination == page3), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult(3));

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole) { PageNumber = 3 };

        await model.OnGetAsync(CancellationToken.None);

        mock.Verify(
            s => s.GetClientsAsync(It.Is<ListQuery>(q => q.Filter == null && q.Pagination == page3),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGetAsync_PageNumberBelowOne_NormalizesToFirstPageBeforeCallingService()
    {
        var mock = new Mock<IClientListService>();
        mock.Setup(s => s.GetClientsAsync(It.IsAny<ListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult());

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole) { PageNumber = 0 };

        await model.OnGetAsync(CancellationToken.None);

        mock.Verify(
            s => s.GetClientsAsync(It.Is<ListQuery>(q => q.Filter == null && q.Pagination == DefaultPagination),
                It.IsAny<CancellationToken>()), Times.Once);
    }
}