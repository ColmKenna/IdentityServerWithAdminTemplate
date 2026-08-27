using IdentityServerProject.Pages.Admin.Apis;
using IdentityServerProject.Services.Apis;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.SecretReveals;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Moq;

namespace IdentityServerProject.Admin.Tests.Apis;

/// <summary>
///     Unit tests for <see cref="EditorModel" /> handler logic, exercised directly against a
///     mocked <see cref="IApiResourceEditorService" /> (no HTTP pipeline involved).
/// </summary>
public class ApiResourceEditorPageModelTests
{
    private static ApiResourceEditorModel MakeEditor(string name = "sales.api", bool isNew = false) => new()
    {
        IsNew = isNew,
        Name = name,
        DisplayName = "Sales API",
        Description = "Desc",
        Enabled = true,
        Secrets = new List<ApiResourceSecretItem>(),
        Scopes = new List<string>(),
        Claims = new List<string>()
    };

    // ---------- OnGetAsync ----------

    [Fact]
    public async Task OnGetAsync_NoName_ReturnsEmptyCreateModeEditorOnBasicsTab()
    {
        var mock = new Mock<IApiResourceEditorService>();
        var model = new EditorModel(mock.Object) { Name = null, Tab = "secrets" };

        IActionResult result = await model.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(model.Editor.IsNew);
        Assert.Equal("basics", model.Tab);
        mock.Verify(s => s.GetForEditAsync(It.IsAny<ScopeName>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnGetAsync_NameResolvesToResource_PopulatesEditor()
    {
        ApiResourceEditorModel editor = MakeEditor();
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.GetForEditAsync(ScopeName.Create("sales.api"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(editor);

        var model = new EditorModel(mock.Object) { Name = "sales.api" };

        IActionResult result = await model.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Same(editor, model.Editor);
        Assert.Equal("Sales API", model.Basics.DisplayName);
    }

    [Fact]
    public async Task OnGetAsync_NameDoesNotResolve_ReturnsNotFound()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.GetForEditAsync(ScopeName.Create("missing"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ApiResourceEditorModel));

        var model = new EditorModel(mock.Object) { Name = "missing" };

        IActionResult result = await model.OnGetAsync(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData("secrets", "secrets")]
    [InlineData("SCOPES", "scopes")]
    [InlineData("claims", "claims")]
    [InlineData("bogus", "basics")]
    [InlineData(null, "basics")]
    public async Task OnGetAsync_TabQueryString_NormalizesToKnownTab(string? requestedTab, string expectedTab)
    {
        ApiResourceEditorModel editor = MakeEditor();
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.GetForEditAsync(ScopeName.Create("sales.api"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(editor);
        mock.Setup(s => s.GetAllApiScopeNamesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());

        var model = new EditorModel(mock.Object) { Name = "sales.api", Tab = requestedTab! };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(expectedTab, model.Tab);
    }

    // ---------- OnPostSaveBasicsAsync ----------

    [Fact]
    public async Task OnPostSaveBasicsAsync_Success_RedirectsToEditorWithSavedNameAndBasicsTab()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.SaveBasicsAsync(It.IsAny<SaveApiResourceBasicsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SaveApiResourceBasicsResult.SucceededResult());

        var model = new EditorModel(mock.Object)
        {
            Name = null,
            Basics = new EditorModel.BasicsInputModel { Name = "new.api" }
        };

        IActionResult result = await model.OnPostSaveBasicsAsync(CancellationToken.None);

        RedirectToPageResult redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("new.api", redirect.RouteValues!["name"]);
        Assert.Equal("basics", redirect.RouteValues!["tab"]);
    }

    [Fact]
    public async Task OnPostSaveBasicsAsync_NameCollision_SetsErrorAndRedisplaysPage()
    {
        ApiResourceEditorModel editor = MakeEditor("original.api");
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.GetForEditAsync(ScopeName.Create("original.api"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(editor);
        mock.Setup(s => s.SaveBasicsAsync(It.IsAny<SaveApiResourceBasicsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SaveApiResourceBasicsResult.ConflictResult("Basics.Name",
                "An API resource named 'taken.api' already exists."));

        var model = new EditorModel(mock.Object)
        {
            Name = "original.api",
            Basics = new EditorModel.BasicsInputModel { Name = "taken.api" }
        };

        IActionResult result = await model.OnPostSaveBasicsAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.True(model.ModelState.ContainsKey("Basics.Name"));
    }

    [Fact]
    public async Task OnPostSaveBasicsAsync_OriginalResourceNotFound_ReturnsNotFound()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.SaveBasicsAsync(It.IsAny<SaveApiResourceBasicsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SaveApiResourceBasicsResult.NotFoundResult());

        var model = new EditorModel(mock.Object)
        {
            Name = "gone.api",
            Basics = new EditorModel.BasicsInputModel { Name = "gone.api" }
        };

        IActionResult result = await model.OnPostSaveBasicsAsync(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ---------- OnPostAddSecretAsync ----------

    [Fact]
    public async Task OnPostAddSecretAsync_Success_StoresOnlyOpaqueHandleAndRedirectsToSecretsTab()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.AddSecretAsync(It.IsAny<AddApiResourceSecretCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResourceAddSecretResult.Succeeded("the-plaintext-secret"));

        var reveals = new Mock<ISecretRevealService>();
        reveals.Setup(service => service.IssueAsync(
                new SecretRevealTarget(SecretRevealPurpose.ApiResourceSecretGenerated, "sales.api"),
                "the-plaintext-secret",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretRevealTicket(SecretRevealHandle.Create("opaque-handle"),
                DateTimeOffset.UtcNow.AddMinutes(5)));

        var model = new EditorModel(mock.Object, reveals.Object)
        {
            Name = "sales.api",
            Secret = new EditorModel.SecretInputModel { Description = "desc" }
        };

        IActionResult result = await model.OnPostAddSecretAsync(CancellationToken.None);

        RedirectToPageResult redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("secrets", redirect.RouteValues!["tab"]);
        Assert.Null(model.GeneratedSecret);
        Assert.Equal("opaque-handle", model.SecretRevealHandle);
    }

    [Fact]
    public async Task OnPostAddSecretAsync_ResourceNotFound_ReturnsNotFound()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.AddSecretAsync(It.IsAny<AddApiResourceSecretCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResourceAddSecretResult.NotFound);

        var model = new EditorModel(mock.Object) { Name = "missing" };

        IActionResult result = await model.OnPostAddSecretAsync(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ---------- OnPostDeleteAsync ----------

    [Fact]
    public async Task OnPostDeleteAsync_Success_RedirectsToIndexPage()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.DeleteAsync(ScopeName.Create("sales.api"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        var model = new EditorModel(mock.Object) { Name = "sales.api" };

        IActionResult result = await model.OnPostDeleteAsync("DELETE", CancellationToken.None);

        RedirectToPageResult redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Index", redirect.PageName);
    }

    [Fact]
    public async Task OnPostDeleteAsync_ResourceNotFound_ReturnsNotFound()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.DeleteAsync(ScopeName.Create("missing"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.NotFoundResult());

        var model = new EditorModel(mock.Object) { Name = "missing" };

        IActionResult result = await model.OnPostDeleteAsync("DELETE", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ---------- OnPostEnableAsync / OnPostDisableAsync ----------

    [Fact]
    public async Task OnPostDisableAsync_Success_RedirectsToBasicsTab()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.SetEnabledAsync(ScopeName.Create("sales.api"), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        var model = new EditorModel(mock.Object) { Name = "sales.api" };

        IActionResult result = await model.OnPostDisableAsync("DISABLE", CancellationToken.None);

        RedirectToPageResult redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("basics", redirect.RouteValues!["tab"]);
    }

    [Fact]
    public async Task OnPostDeleteAsync_InvalidConfirmation_RedirectsWithoutDeleting()
    {
        var mock = new Mock<IApiResourceEditorService>();
        var model = new EditorModel(mock.Object) { Name = "sales.api" };

        IActionResult result = await model.OnPostDeleteAsync("delete", CancellationToken.None);

        RedirectToPageResult redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("sales.api", redirect.RouteValues!["name"]);
        Assert.Contains("Type DELETE", model.ErrorMessage);
        mock.Verify(s => s.DeleteAsync(It.IsAny<ScopeName>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnPostEnableAsync_ResourceNotFound_ReturnsNotFound()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.SetEnabledAsync(ScopeName.Create("missing"), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.NotFoundResult());

        var model = new EditorModel(mock.Object) { Name = "missing" };

        IActionResult result = await model.OnPostEnableAsync(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ---------- Scope & claim handlers ----------

    [Fact]
    public async Task OnPostAttachScopeAsync_Success_RedirectsToScopesTab()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.AttachScopeAsync(ScopeName.Create("sales.api"), ScopeName.Create("sales.read"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        var model = new EditorModel(mock.Object)
        {
            Name = "sales.api",
            AttachScope = new EditorModel.AttachScopeInputModel { ScopeName = "sales.read" }
        };

        IActionResult result = await model.OnPostAttachScopeAsync(CancellationToken.None);

        RedirectToPageResult redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("scopes", redirect.RouteValues!["tab"]);
    }

    [Fact]
    public async Task OnPostDetachScopeAsync_ScopeNotAttached_ReturnsNotFound()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.DetachScopeAsync(ScopeName.Create("sales.api"), ScopeName.Create("sales.read"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.NotFoundResult());

        var model = new EditorModel(mock.Object) { Name = "sales.api" };

        IActionResult result = await model.OnPostDetachScopeAsync("sales.read", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPostAddClaimAsync_Success_RedirectsToClaimsTab()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.AddClaimAsync(It.IsAny<AddApiResourceClaimCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        var model = new EditorModel(mock.Object)
        {
            Name = "sales.api",
            Claim = new EditorModel.ClaimInputModel { ClaimType = "department" }
        };

        IActionResult result = await model.OnPostAddClaimAsync(CancellationToken.None);

        RedirectToPageResult redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("claims", redirect.RouteValues!["tab"]);
    }

    [Fact]
    public async Task OnPostRemoveClaimAsync_ValidationFailure_ReloadsEditorAndRedisplaysClaimsTab()
    {
        ApiResourceEditorModel editor = MakeEditor();
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s =>
                s.RemoveClaimAsync(ScopeName.Create("sales.api"), ClaimType.Create("sub"),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.ValidationFailure("Claim.ClaimType", "The sub claim cannot be removed."));
        mock.Setup(s => s.GetForEditAsync(ScopeName.Create("sales.api"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(editor);
        mock.Setup(s => s.GetAllApiScopeNamesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());

        var model = new EditorModel(mock.Object) { Name = "sales.api", Tab = "basics" };

        IActionResult result = await model.OnPostRemoveClaimAsync("sub", CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Same(editor, model.Editor);
        Assert.Equal("claims", model.Tab);
        Assert.Equal("The sub claim cannot be removed.",
            model.ModelState["Claim.ClaimType"]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task OnPostRevokeSecretAsync_ValidationFailure_ReloadsEditorAndRedisplaysSecretsTab()
    {
        ApiResourceEditorModel editor = MakeEditor();
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.RevokeSecretAsync(ScopeName.Create("sales.api"), 17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.ValidationFailure(string.Empty, "The secret cannot be revoked."));
        mock.Setup(s => s.GetForEditAsync(ScopeName.Create("sales.api"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(editor);
        mock.Setup(s => s.GetAllApiScopeNamesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());

        var model = new EditorModel(mock.Object) { Name = "sales.api" };

        IActionResult result = await model.OnPostRevokeSecretAsync(17, "REVOKE", CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Same(editor, model.Editor);
        Assert.Equal("secrets", model.Tab);
        Assert.Equal("The secret cannot be revoked.", model.ModelState[string.Empty]!.Errors.Single().ErrorMessage);
    }

    // ---------- OnPostSaveBasicsAsync (invalid ModelState) ----------

    [Fact]
    public async Task OnPostSaveBasicsAsync_InvalidModelState_ReloadsEditorAndRedisplaysBasicsTabWithoutSaving()
    {
        ApiResourceEditorModel editor = MakeEditor();
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.GetForEditAsync(ScopeName.Create("sales.api"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(editor);
        mock.Setup(s => s.SaveBasicsAsync(It.IsAny<SaveApiResourceBasicsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SaveApiResourceBasicsResult.ValidationFailure("Basics.Name", "The Name field is required."));

        var model = new EditorModel(mock.Object)
        {
            Name = "sales.api",
            Tab = "secrets",
            Basics = new EditorModel.BasicsInputModel { Name = string.Empty }
        };

        IActionResult result = await model.OnPostSaveBasicsAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Same(editor, model.Editor);
        Assert.Equal("basics", model.Tab);
    }

    [Fact]
    public async Task OnPostSaveBasicsAsync_StronglyTypedValidationErrors_AddsFieldErrorsToModelState()
    {
        ApiResourceEditorModel editor = MakeEditor();
        var validationErrors = new ApiResourceBasicsValidationErrors();
        validationErrors.AddNameError("Name is required.");
        validationErrors.AddDisplayNameError("Display name is invalid.");
        validationErrors.AddDescriptionError("Description is too long.");

        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.GetForEditAsync(ScopeName.Create("sales.api"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(editor);
        mock.Setup(s => s.SaveBasicsAsync(It.IsAny<SaveApiResourceBasicsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SaveApiResourceBasicsResult.ValidationFailure(validationErrors));

        var model = new EditorModel(mock.Object)
        {
            Name = "sales.api",
            Basics = new EditorModel.BasicsInputModel { Name = string.Empty }
        };

        IActionResult result = await model.OnPostSaveBasicsAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.True(model.ModelState.ContainsKey("Basics.Name"));
        Assert.True(model.ModelState.ContainsKey("Basics.DisplayName"));
        Assert.True(model.ModelState.ContainsKey("Basics.Description"));
    }

    // ---------- OnPostCreateScopeAsync ----------

    [Fact]
    public async Task OnPostCreateScopeAsync_NameCollision_SetsErrorAndRedisplaysPage()
    {
        ApiResourceEditorModel editor = MakeEditor();
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.GetForEditAsync(ScopeName.Create("sales.api"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(editor);
        mock.Setup(s => s.GetAllApiScopeNamesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        mock.Setup(s => s.CreateScopeAsync(It.IsAny<CreateApiResourceScopeCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.ConflictResult("CreateScope.ScopeName",
                "A scope named 'taken.scope' already exists."));

        var model = new EditorModel(mock.Object)
        {
            Name = "sales.api",
            CreateScope = new EditorModel.CreateScopeInputModel { ScopeName = "taken.scope" }
        };

        IActionResult result = await model.OnPostCreateScopeAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.True(model.ModelState.ContainsKey("CreateScope.ScopeName"));
    }

    [Fact]
    public async Task OnPostCreateScopeAsync_ResourceNotFound_ReturnsNotFound()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.CreateScopeAsync(It.IsAny<CreateApiResourceScopeCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.NotFoundResult());

        var model = new EditorModel(mock.Object)
        {
            Name = "missing",
            CreateScope = new EditorModel.CreateScopeInputModel { ScopeName = "new.scope" }
        };

        IActionResult result = await model.OnPostCreateScopeAsync(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}