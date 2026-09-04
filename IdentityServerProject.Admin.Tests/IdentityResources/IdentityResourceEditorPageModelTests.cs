using IdentityServerProject.Pages.Admin.IdentityResources;
using IdentityServerProject.Pages.Shared;
using IdentityServerProject.Services.IdentityResources;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Moq;

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
        service.Setup(s => s.GetForEditAsync(ScopeName.Create("profile"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Editor());
        var model = new EditModel(service.Object) { Name = "profile" };
        model.ModelState.AddModelError("Input.DisplayName", "Too long");

        IActionResult result = await model.OnPostSaveAsync(CancellationToken.None);

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
        service.Setup(s =>
                s.RemoveClaimAsync(ScopeName.Create("openid"), ClaimType.Create("sub"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResourceEditResult.Protected("The openid resource must retain the sub claim."));
        service.Setup(s => s.GetForEditAsync(ScopeName.Create("openid"), It.IsAny<CancellationToken>())).ReturnsAsync(
            new IdentityResourceEditorModel
            {
                Name = "openid",
                DisplayName = "OpenID",
                Enabled = true,
                ShowInDiscoveryDocument = true,
                UserClaims = new List<string> { "sub" }
            });
        var model = new EditModel(service.Object);

        IActionResult result = await model.OnPostRemoveClaimAsync("openid", "sub", CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("OpenID", model.Input.DisplayName);
        Assert.Equal("The openid resource must retain the sub claim.",
            model.ModelState[string.Empty]!.Errors.Single().ErrorMessage);
    }

    // ---------- Claim chip locking (B-004) ----------
    //
    // These are the point of moving the projection off the view: the rule deciding which claims a
    // resource may not lose is now reachable without rendering a page. The same three rules are
    // enforced by IdentityResourceEditorService.RemoveClaimCoreAsync, which is what actually
    // refuses — a locked chip is an affordance, not the guard.

    private static EditModel ModelFor(IdentityResourceEditorModel editor)
    {
        var service = new Mock<IIdentityResourceEditorService>();
        service.Setup(s => s.GetForEditAsync(ScopeName.Create(editor.Name), It.IsAny<CancellationToken>()))
            .ReturnsAsync(editor);
        var model = new EditModel(service.Object) { Name = editor.Name };
        model.OnGetAsync(CancellationToken.None).GetAwaiter().GetResult();
        return model;
    }

    [Fact]
    public void ClaimChips_OrdinaryClaimOnUnprotectedResource_IsRemovable()
    {
        ScopeChipItem chip = Assert.Single(ModelFor(Editor()).ClaimChips);

        Assert.Equal("name", chip.Text);
        Assert.False(chip.IsLocked);
    }

    [Fact]
    public void ClaimChips_InvariantClaim_IsLockedWithThePolicysOwnMessage()
    {
        IdentityResourceEditorModel editor = Editor();
        editor.Name = "openid";
        editor.UserClaims = new List<string> { "sub" };

        ScopeChipItem chip = Assert.Single(ModelFor(editor).ClaimChips);

        Assert.True(chip.IsLocked);
        Assert.Equal(BuiltInIdentityResourcePolicy.InvariantClaimMessage("openid", "sub"), chip.LockReason);
    }

    [Fact]
    public void ClaimChips_ProtectedResource_LocksEveryClaim()
    {
        IdentityResourceEditorModel editor = Editor();
        editor.IsProtected = true;
        editor.UserClaims = new List<string> { "name", "email" };

        Assert.All(ModelFor(editor).ClaimChips, chip => Assert.True(chip.IsLocked));
    }

    [Fact]
    public void ClaimChips_InvariantClaimBeatsTheGeneralProtectedReason()
    {
        // Ordering matters: "openid needs sub" is more useful than "this resource is protected",
        // and the service reports them in the same order.
        IdentityResourceEditorModel editor = Editor();
        editor.Name = "openid";
        editor.IsProtected = true;
        editor.UserClaims = new List<string> { "sub" };

        ScopeChipItem chip = Assert.Single(ModelFor(editor).ClaimChips);

        Assert.Contains("required by", chip.LockReason);
        Assert.DoesNotContain("cannot be modified", chip.LockReason);
    }
}