using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Pages.Admin.Grants;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Grants;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace IdentityServerProject.Admin.Tests.Grants;

public class GrantsIndexPageModelTests
{
    private static Pagination DefaultPagination => Pagination.From(1, TestOptions.PageSize);

    [Fact]
    public async Task OnGetAsync_PopulatesGrantsFromService()
    {
        var mockService = new Mock<IGrantListService>();
        var expectedResult = new ListResult<GrantListItem>
        {
            Items = new List<GrantListItem>
            {
                new GrantListItem
                {
                    Key = GrantKey.Create("grant-123"),
                    Type = "user_consent",
                    SubjectId = UserId.Create("user-456"),
                    SessionId = "session-789",
                    ClientId = ClientId.Create("client-app"),
                    ClientName = "Client App",
                    Description = "Consent grant",
                    CreationTime = DateTime.UtcNow,
                    Expiration = DateTime.UtcNow.AddDays(1),
                    ExpirationFormatted = "in 1 day",
                    IsExpired = false
                }
            },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10
        };

        mockService
            .Setup(s => s.GetGrantsAsync(It.IsAny<GrantFilter?>(), DefaultPagination, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var model = new IndexModel(mockService.Object, TestOptions.AdminConsole);

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(expectedResult, model.Grants);
    }

    [Fact]
    public async Task OnGetAsync_PassesBoundFiltersToService()
    {
        var page2 = Pagination.From(2, TestOptions.PageSize);
        var expectedFilter = new GrantFilter(UserId.Create("user-456"), ClientId.Create("client-app"), "refresh_token");
        var mockService = new Mock<IGrantListService>();
        mockService
            .Setup(s => s.GetGrantsAsync(expectedFilter, page2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ListResult<GrantListItem>.Empty(2, TestOptions.PageSize));

        var model = new IndexModel(mockService.Object, TestOptions.AdminConsole)
        {
            SubjectId = "user-456",
            ClientId = "client-app",
            TypeFilter = "refresh_token",
            PageNumber = 2,
        };

        await model.OnGetAsync(CancellationToken.None);

        mockService.Verify(s => s.GetGrantsAsync(expectedFilter, page2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnPostRevokeAsync_WhenRevoked_SetsSuccessMessageAndRedirects()
    {
        var mockService = new Mock<IGrantListService>();
        mockService
            .Setup(s => s.RevokeGrantAsync(GrantKey.Create("grant-123"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RevokeGrantResult.Revoked);

        var model = new IndexModel(mockService.Object, TestOptions.AdminConsole)
        {
            PageNumber = 2,
            SubjectId = "user-456",
            ClientId = "client-app",
            TypeFilter = "refresh_token",
        };

        var result = await model.OnPostRevokeAsync(GrantKey.Create("grant-123"), CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.NotNull(model.SuccessMessage);
        Assert.Contains("revoked successfully", model.SuccessMessage);
        Assert.Equal(2, redirect.RouteValues!["PageNumber"]);
        Assert.Equal("user-456", redirect.RouteValues["SubjectId"]);
        Assert.Equal("client-app", redirect.RouteValues["ClientId"]);
        Assert.Equal("refresh_token", redirect.RouteValues["TypeFilter"]);
    }

    [Fact]
    public async Task OnPostRevokeAsync_WhenNotFound_ReturnsNotFoundResult()
    {
        var mockService = new Mock<IGrantListService>();
        mockService
            .Setup(s => s.RevokeGrantAsync(GrantKey.Create("nonexistent"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RevokeGrantResult.NotFound);

        var model = new IndexModel(mockService.Object, TestOptions.AdminConsole);

        var result = await model.OnPostRevokeAsync(GrantKey.Create("nonexistent"), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
