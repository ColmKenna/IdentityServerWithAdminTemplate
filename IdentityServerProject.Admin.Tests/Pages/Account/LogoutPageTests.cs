using Duende.IdentityServer.Events;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using IdentityServerProject.Data;
using IdentityServerProject.Pages.Account;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Moq;

namespace IdentityServerProject.Admin.Tests.Pages.Account;

public class LogoutPageTests
{
    [Fact]
    public async Task OnPostAsync_WhenSignOutIFrameUrlExists_ReturnsPageResultWithIFrameUrl()
    {
        var mockSignInManager = new Mock<SignInManager<ApplicationUser>>(
            new Mock<UserManager<ApplicationUser>>(
                new Mock<IUserStore<ApplicationUser>>().Object, null!, null!, null!, null!, null!, null!, null!, null!).Object,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>().Object,
            null!, null!, null!, null!);

        var mockInteraction = new Mock<IIdentityServerInteractionService>();
        var mockEvents = new Mock<IEventService>();

        string logoutId = "logout-123";
        string expectedIFrameUrl = "https://client.local/signout-oidc";

        mockInteraction
            .Setup(i => i.GetLogoutContextAsync(logoutId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogoutRequest(expectedIFrameUrl, (LogoutMessage?)null));

        var pageModel = new LogoutModel(mockSignInManager.Object, mockInteraction.Object, mockEvents.Object)
        {
            LogoutId = logoutId,
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
        };

        IActionResult result = await pageModel.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(expectedIFrameUrl, pageModel.SignOutIFrameUrl);
        Assert.False(pageModel.ShowLogoutPrompt);
    }

    [Fact]
    public async Task OnPostAsync_WhenNoSignOutIFrameUrlAndPostLogoutRedirectUriExists_RedirectsToUri()
    {
        var mockSignInManager = new Mock<SignInManager<ApplicationUser>>(
            new Mock<UserManager<ApplicationUser>>(
                new Mock<IUserStore<ApplicationUser>>().Object, null!, null!, null!, null!, null!, null!, null!, null!).Object,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>().Object,
            null!, null!, null!, null!);

        var mockInteraction = new Mock<IIdentityServerInteractionService>();
        var mockEvents = new Mock<IEventService>();

        string logoutId = "logout-456";
        string redirectUri = "https://client.local/post-logout";

        mockInteraction
            .Setup(i => i.GetLogoutContextAsync(logoutId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogoutRequest((string?)null!, new LogoutMessage { PostLogoutRedirectUri = redirectUri }));

        var pageModel = new LogoutModel(mockSignInManager.Object, mockInteraction.Object, mockEvents.Object)
        {
            LogoutId = logoutId,
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
        };

        IActionResult result = await pageModel.OnPostAsync();

        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal(redirectUri, redirect.Url);
    }

    [Fact]
    public async Task OnPostAsync_WhenNoIFrameAndNoRedirectUri_ReturnsPageResult()
    {
        var mockSignInManager = new Mock<SignInManager<ApplicationUser>>(
            new Mock<UserManager<ApplicationUser>>(
                new Mock<IUserStore<ApplicationUser>>().Object, null!, null!, null!, null!, null!, null!, null!, null!).Object,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>().Object,
            null!, null!, null!, null!);

        var mockInteraction = new Mock<IIdentityServerInteractionService>();
        var mockEvents = new Mock<IEventService>();

        string logoutId = "logout-789";

        mockInteraction
            .Setup(i => i.GetLogoutContextAsync(logoutId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogoutRequest((string?)null!, (LogoutMessage?)null));

        var pageModel = new LogoutModel(mockSignInManager.Object, mockInteraction.Object, mockEvents.Object)
        {
            LogoutId = logoutId,
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
        };

        IActionResult result = await pageModel.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Null(pageModel.SignOutIFrameUrl);
        Assert.False(pageModel.ShowLogoutPrompt);
    }
}
