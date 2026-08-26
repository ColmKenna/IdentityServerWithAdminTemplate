using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Pages.Admin.IdentityResources;
using IdentityServerProject.Services.IdentityResources;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.IdentityResources;

public class IdentityResourceEditorPageModelTests
{
    private static IdentityResourceEditorModel Editor() => new()
    {
        Name = "profile",
        DisplayName = "Profile",
        Description = "Profile data",
        Enabled = true,
        Required = false,
        Emphasize = true,
        ShowInDiscoveryDocument = true,
        UserClaims = new List<string> { "name" }
    };

    [Fact]
    public async Task OnPostSaveAsync_InvalidModelState_ReloadsEditorWithoutUpdating()
    {
        var service = new Mock<IIdentityResourceEditorService>();
        service.Setup(s => s.GetForEditAsync(ScopeName.Create("profile"), It.IsAny<CancellationToken>())).ReturnsAsync(Editor());
        var model = new EditModel(service.Object) { Name = "profile" };
        model.ModelState.AddModelError("Input.DisplayName", "Too long");

        var result = await model.OnPostSaveAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Profile", model.Editor.DisplayName);
        Assert.Null(model.Input.DisplayName);
        service.Verify(s => s.UpdateBasicsAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(),
            It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnPostRemoveClaimAsync_ProtectedResource_ReloadsEditorAndShowsServiceReason()
    {
        var service = new Mock<IIdentityResourceEditorService>();
        service.Setup(s => s.RemoveClaimAsync(ScopeName.Create("openid"), ClaimType.Create("sub"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResourceEditResult.Protected("The openid resource must retain the sub claim."));
        service.Setup(s => s.GetForEditAsync(ScopeName.Create("openid"), It.IsAny<CancellationToken>())).ReturnsAsync(new IdentityResourceEditorModel
        {
            Name = "openid",
            DisplayName = "OpenID",
            Enabled = true,
            ShowInDiscoveryDocument = true,
            UserClaims = new List<string> { "sub" }
        });
        var model = new EditModel(service.Object);

        var result = await model.OnPostRemoveClaimAsync("openid", "sub", CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("OpenID", model.Input.DisplayName);
        Assert.Equal("The openid resource must retain the sub claim.", model.ModelState[string.Empty]!.Errors.Single().ErrorMessage);
    }
}
