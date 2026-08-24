using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Pages.Admin.Grants;
using IdentityServerProject.Services.Grants;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Grants;

public class GrantsIndexPageModelTests
{
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
                    Key = "grant-123",
                    Type = "user_consent",
                    SubjectId = "user-456",
                    SessionId = "session-789",
                    ClientId = "client-app",
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
            .Setup(s => s.GetGrantsAsync(null, null, null, 1, TestOptions.PageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var model = new IndexModel(mockService.Object, TestOptions.AdminConsole);

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(expectedResult, model.Grants);
    }

    [Fact]
    public async Task OnGetAsync_PassesBoundFiltersToService()
    {
        var mockService = new Mock<IGrantListService>();
        mockService
            .Setup(s => s.GetGrantsAsync("user-456", "client-app", "refresh_token", 2, TestOptions.PageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ListResult<GrantListItem>.Empty(2, TestOptions.PageSize));

        var model = new IndexModel(mockService.Object, TestOptions.AdminConsole)
        {
            SubjectId = "user-456",
            ClientId = "client-app",
            TypeFilter = "refresh_token",
            PageNumber = 2,
        };

        await model.OnGetAsync(CancellationToken.None);

        mockService.Verify(s => s.GetGrantsAsync("user-456", "client-app", "refresh_token", 2, TestOptions.PageSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnPostRevokeAsync_WhenRevoked_SetsSuccessMessageAndRedirects()
    {
        var mockService = new Mock<IGrantListService>();
        mockService
            .Setup(s => s.RevokeGrantAsync("grant-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RevokeGrantResult.Revoked);

        var model = new IndexModel(mockService.Object, TestOptions.AdminConsole)
        {
            PageNumber = 2,
            SubjectId = "user-456",
            ClientId = "client-app",
            TypeFilter = "refresh_token",
        };

        var result = await model.OnPostRevokeAsync("grant-123", CancellationToken.None);

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
            .Setup(s => s.RevokeGrantAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RevokeGrantResult.NotFound);

        var model = new IndexModel(mockService.Object, TestOptions.AdminConsole);

        var result = await model.OnPostRevokeAsync("nonexistent", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}


