using IdentityServerProject.Admin.Tests.Infrastructure;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Pages.Admin.Users;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Users;

/// <summary>
/// Unit tests for <see cref="IndexModel"/> handler logic, exercised directly
/// against a mocked <see cref="IUserListService"/> (no HTTP pipeline involved).
/// </summary>
public class UsersIndexPageModelTests
{
    private static ListResult<UserListItem> MakeResult(int pageNumber = 1, int pageSize = 10) => new()
    {
        Items = new[]
        {
            new UserListItem { Id = "u1", UserName = "admin@sales.local", Email = "admin@sales.local", FullName = "Sys Admin", IsLockedOut = false, LockoutEnd = null },
        },
        TotalCount = 1,
        PageNumber = pageNumber,
        PageSize = pageSize,
    };

    [Fact]
    public async Task OnGetAsync_NoQueryParameters_CallsServiceWithNullFilterAndFirstPage()
    {
        var mock = new Mock<IUserListService>();
        mock.Setup(s => s.GetUsersAsync(null, 1, TestOptions.PageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult());

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole);

        await model.OnGetAsync(CancellationToken.None);

        mock.Verify(s => s.GetUsersAsync(null, 1, TestOptions.PageSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGetAsync_FilterSpecified_PassesFilterToService()
    {
        var mock = new Mock<IUserListService>();
        mock.Setup(s => s.GetUsersAsync("admin", 1, TestOptions.PageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult());

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole) { Filter = "admin" };

        await model.OnGetAsync(CancellationToken.None);

        mock.Verify(s => s.GetUsersAsync("admin", 1, TestOptions.PageSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGetAsync_ServiceReturnsResult_PopulatesUsersProperty()
    {
        var expected = MakeResult();
        var mock = new Mock<IUserListService>();
        mock.Setup(s => s.GetUsersAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole);

        await model.OnGetAsync(CancellationToken.None);

        Assert.Same(expected, model.Users);
    }

    [Fact]
    public async Task OnPostUnlockAsync_ValidUserId_UnlocksUserAndRedirectsToPage()
    {
        var mock = new Mock<IUserListService>();
        mock.Setup(s => s.UnlockUserAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserUnlockResult.Succeeded);

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole);

        var result = await model.OnPostUnlockAsync("u1", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.NotNull(model.StatusMessage);
        mock.Verify(s => s.UnlockUserAsync("u1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnPostUnlockAsync_InvalidUserId_ReturnsNotFound()
    {
        var mock = new Mock<IUserListService>();
        mock.Setup(s => s.UnlockUserAsync("invalid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserUnlockResult.NotFound);

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole);

        var result = await model.OnPostUnlockAsync("invalid", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPostUnlockAsync_IdentityOperationFails_ShowsOperatorErrorAndRedirects()
    {
        var mock = new Mock<IUserListService>();
        mock.Setup(s => s.UnlockUserAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserUnlockResult(
                UserUnlockStatus.Failed,
                new[] { "The lockout state could not be updated." }));

        var model = new IndexModel(mock.Object, TestOptions.AdminConsole);

        var result = await model.OnPostUnlockAsync("u1", CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("could not be updated", model.StatusMessage);
    }
}


