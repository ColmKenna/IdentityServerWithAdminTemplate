using System.Security.Claims;
using IdentityServerProject.Pages.Admin.Users;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Moq;

namespace IdentityServerProject.Admin.Tests.Users;

public class UsersDetailsPageModelTests
{
    private static DetailsModel CreateModel(Mock<IUserDetailsService> service, string? currentUserId = "admin")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            currentUserId == null
                ? Array.Empty<Claim>()
                : new[] { new Claim(ClaimTypes.NameIdentifier, currentUserId) },
            "test"));

        var modelState = new ModelStateDictionary();
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), modelState);
        var pageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState)
        };

        return new DetailsModel(service.Object)
        {
            PageContext = pageContext,
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };
    }

    [Fact]
    public async Task OnPostUnlockAsync_FailureWithoutServiceErrors_RedirectsWithDefaultErrorMessage()
    {
        var service = new Mock<IUserDetailsService>();
        service.Setup(s => s.UnlockUserAsync(UserId.Create("user-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserUnlockResult(UserUnlockStatus.Failed, Array.Empty<string>()));
        DetailsModel model = CreateModel(service);
        model.Id = "user-1";

        IActionResult result = await model.OnPostUnlockAsync(CancellationToken.None);

        RedirectToPageResult redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("overview", redirect.RouteValues!["tab"]);
        Assert.Equal("Unable to unlock the user account.", model.ErrorMessage);
    }

    [Fact]
    public async Task OnPostAddRoleAsync_MissingRole_ReturnsNotFound()
    {
        var service = new Mock<IUserDetailsService>();
        service.Setup(s => s.AddRoleAsync(UserId.Create("user-1"), "missing-role", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleChangeResult.Failed("Role not found."));
        DetailsModel model = CreateModel(service);
        model.Id = "user-1";

        IActionResult result = await model.OnPostAddRoleAsync("missing-role", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPostRevokeUserAccessAsync_ServiceFailure_RedirectsToAccessWithServiceError()
    {
        var service = new Mock<IUserDetailsService>();
        service.Setup(s =>
                s.RevokeUserAccessAsync(new UserActionContext(UserId.Create("user-1"), UserId.Create("admin")),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserAccessRevokeResult.Failed("The current administrator cannot revoke their own access."));
        DetailsModel model = CreateModel(service);
        model.Id = "user-1";

        IActionResult result = await model.OnPostRevokeUserAccessAsync(CancellationToken.None);

        RedirectToPageResult redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("access", redirect.RouteValues!["tab"]);
        Assert.Equal("The current administrator cannot revoke their own access.", model.ErrorMessage);
    }

    [Fact]
    public async Task OnPostDeleteAsync_InvalidConfirmation_RedirectsWithoutCallingService()
    {
        var service = new Mock<IUserDetailsService>();
        DetailsModel model = CreateModel(service);
        model.Id = "user-1";
        model.DeleteConfirmation = "delete";

        IActionResult result = await model.OnPostDeleteAsync(CancellationToken.None);

        RedirectToPageResult redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("danger", redirect.RouteValues!["tab"]);
        Assert.Equal("Type DELETE exactly to confirm permanent deletion.", model.ErrorMessage);
        service.Verify(s => s.DeleteUserAsync(It.IsAny<UserActionContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}