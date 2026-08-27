using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Pages.Admin.IdentityResources;
using IdentityServerProject.Services.IdentityResources;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace IdentityServerProject.Admin.Tests.IdentityResources;

public class IdentityResourcesIndexPageModelTests
{
    private static Pagination DefaultPagination => Pagination.From(1, TestOptions.PageSize);

    [Fact]
    public async Task OnGetAsync_PopulatesIdentityResourcesFromService()
    {
        var mockService = new Mock<IIdentityResourceListService>();
        var expectedResult = new ListResult<IdentityResourceListItem>
        {
            Items = new List<IdentityResourceListItem>
            {
                new IdentityResourceListItem
                {
                    Name = "openid",
                    DisplayName = "OpenID",
                    Description = "Sub claim",
                    Enabled = true,
                    Required = true,
                    Emphasize = false,
                    ShowInDiscoveryDocument = true,
                    UserClaimsCount = 1,
                    ClientReferenceCount = 2,
                    NonEditable = true
                }
            },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10
        };

        mockService
            .Setup(s => s.GetIdentityResourcesAsync(new ListQuery(null, DefaultPagination), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var model = new IndexModel(mockService.Object, TestOptions.AdminConsole);

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(expectedResult, model.IdentityResources);
    }

    [Fact]
    public async Task OnGetAsync_FilterSet_PassesFilterToService()
    {
        var mockService = new Mock<IIdentityResourceListService>();
        mockService
            .Setup(s => s.GetIdentityResourcesAsync(new ListQuery("profile", DefaultPagination), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ListResult<IdentityResourceListItem>.Empty(1, TestOptions.PageSize));
        var model = new IndexModel(mockService.Object, TestOptions.AdminConsole) { Filter = "profile" };

        await model.OnGetAsync(CancellationToken.None);

        mockService.Verify(s => s.GetIdentityResourcesAsync(new ListQuery("profile", DefaultPagination), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnPostDeleteAsync_WhenBlocked_SetsDeleteErrorMessageAndRedirects()
    {
        var mockService = new Mock<IIdentityResourceListService>();
        mockService
            .Setup(s => s.DeleteIdentityResourceAsync("openid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResourceDeleteResult.Blocked);

        var model = new IndexModel(mockService.Object, TestOptions.AdminConsole);

        var result = await model.OnPostDeleteAsync("openid", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.NotNull(model.DeleteErrorMessage);
        Assert.Contains("openid", model.DeleteErrorMessage);
    }

    [Fact]
    public async Task OnPostDeleteAsync_WhenNotFound_ReturnsNotFoundResult()
    {
        var mockService = new Mock<IIdentityResourceListService>();
        mockService
            .Setup(s => s.DeleteIdentityResourceAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResourceDeleteResult.NotFound);

        var model = new IndexModel(mockService.Object, TestOptions.AdminConsole);

        var result = await model.OnPostDeleteAsync("nonexistent", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
