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

public class ResetPasswordPageModelTests
{
    private static UserDetailsModel Account(string fullName = "Ada Admin") => new()
    {
        Id = "user-1",
        UserName = "ada",
        FullName = fullName
    };

    private static ResetPasswordModel CreateModel(Mock<IUserDetailsService> service)
    {
        var httpContext = new DefaultHttpContext();
        var modelState = new ModelStateDictionary();
        var pageContext =
            new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), modelState))
            {
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState)
            };

        return new ResetPasswordModel(service.Object)
        {
            PageContext = pageContext,
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };
    }

    [Fact]
    public async Task OnPostAsync_InvalidModelState_ReloadsDisplayNameWithoutCallingResetService()
    {
        var service = new Mock<IUserDetailsService>();
        service.Setup(s =>
                s.GetUserDetailsAsync(new UserActionContext(UserId.Create("user-1"), null),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(Account());
        ResetPasswordModel model = CreateModel(service);
        model.Id = "user-1";
        model.ModelState.AddModelError("Input.ConfirmPassword", "The password and confirmation password do not match.");

        IActionResult result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Ada Admin", model.UserNameDisplay);
        service.Verify(s => s.ResetPasswordAsync(It.IsAny<UserId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnPostAsync_ServiceFailure_ReloadsDisplayNameAndAddsTheServiceError()
    {
        var service = new Mock<IUserDetailsService>();
        service.Setup(s => s.ResetPasswordAsync(UserId.Create("user-1"), "new-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PasswordResetResult.Failed("The password does not meet the configured policy."));
        service.Setup(s =>
                s.GetUserDetailsAsync(new UserActionContext(UserId.Create("user-1"), null),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(Account());
        ResetPasswordModel model = CreateModel(service);
        model.Id = "user-1";
        model.Input = new ResetPasswordInputModel { NewPassword = "new-password", ConfirmPassword = "new-password" };

        IActionResult result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Ada Admin", model.UserNameDisplay);
        Assert.Equal("The password does not meet the configured policy.",
            model.ModelState[string.Empty]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task OnPostAsync_Success_SetsStatusMessageAndRedirectsToDetails()
    {
        var service = new Mock<IUserDetailsService>();
        service.Setup(s => s.ResetPasswordAsync(UserId.Create("user-1"), "new-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PasswordResetResult.Succeeded());
        ResetPasswordModel model = CreateModel(service);
        model.Id = "user-1";
        model.Input = new ResetPasswordInputModel { NewPassword = "new-password", ConfirmPassword = "new-password" };

        IActionResult result = await model.OnPostAsync(CancellationToken.None);

        RedirectToPageResult redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Details", redirect.PageName);
        Assert.Equal("user-1", redirect.RouteValues!["id"]);
        Assert.Equal("Password has been successfully reset.", model.TempData["StatusMessage"]);
    }
}