using IdentityServerProject.Admin.Tests.Infrastructure;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Pages.Admin.Apis;
using IdentityServerProject.Services.Apis;
using IdentityServerProject.Services.SecretReveals;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Apis;

/// <summary>
/// Unit tests for <see cref="EditorModel"/> handler logic, exercised directly against a
/// mocked <see cref="IApiResourceEditorService"/> (no HTTP pipeline involved).
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
        Secrets = new(),
        Scopes = new(),
        Claims = new(),
    };

    // ---------- OnGetAsync ----------

    [Fact]
    public async Task OnGetAsync_NoName_ReturnsEmptyCreateModeEditorOnBasicsTab()
    {
        var mock = new Mock<IApiResourceEditorService>();
        var model = new EditorModel(mock.Object) { Name = null, Tab = "secrets" };

        var result = await model.OnGetAsync(cancellationToken: CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(model.Editor.IsNew);
        Assert.Equal("basics", model.Tab);
        mock.Verify(s => s.GetForEditAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnGetAsync_NameResolvesToResource_PopulatesEditor()
    {
        var editor = MakeEditor();
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.GetForEditAsync("sales.api", It.IsAny<CancellationToken>())).ReturnsAsync(editor);

        var model = new EditorModel(mock.Object) { Name = "sales.api" };

        var result = await model.OnGetAsync(cancellationToken: CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Same(editor, model.Editor);
        Assert.Equal("Sales API", model.Basics.DisplayName);
    }

    [Fact]
    public async Task OnGetAsync_NameDoesNotResolve_ReturnsNotFound()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.GetForEditAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync(default(ApiResourceEditorModel));

        var model = new EditorModel(mock.Object) { Name = "missing" };

        var result = await model.OnGetAsync(cancellationToken: CancellationToken.None);

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
        var editor = MakeEditor();
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.GetForEditAsync("sales.api", It.IsAny<CancellationToken>())).ReturnsAsync(editor);
        mock.Setup(s => s.GetAllApiScopeNamesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());

        var model = new EditorModel(mock.Object) { Name = "sales.api", Tab = requestedTab! };

        await model.OnGetAsync(cancellationToken: CancellationToken.None);

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
            Basics = new EditorModel.BasicsInputModel { Name = "new.api" },
        };

        var result = await model.OnPostSaveBasicsAsync(cancellationToken: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("new.api", redirect.RouteValues!["name"]);
        Assert.Equal("basics", redirect.RouteValues!["tab"]);
    }

    [Fact]
    public async Task OnPostSaveBasicsAsync_NameCollision_SetsErrorAndRedisplaysPage()
    {
        var editor = MakeEditor("original.api");
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.GetForEditAsync("original.api", It.IsAny<CancellationToken>())).ReturnsAsync(editor);
        mock.Setup(s => s.SaveBasicsAsync(It.IsAny<SaveApiResourceBasicsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SaveApiResourceBasicsResult.ConflictResult("Basics.Name", "An API resource named 'taken.api' already exists."));

        var model = new EditorModel(mock.Object)
        {
            Name = "original.api",
            Basics = new EditorModel.BasicsInputModel { Name = "taken.api" },
        };

        var result = await model.OnPostSaveBasicsAsync(cancellationToken: CancellationToken.None);

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
            Basics = new EditorModel.BasicsInputModel { Name = "gone.api" },
        };

        var result = await model.OnPostSaveBasicsAsync(cancellationToken: CancellationToken.None);

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
                SecretRevealPurpose.ApiResourceSecretGenerated,
                "sales.api",
                "the-plaintext-secret",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretRevealTicket("opaque-handle", DateTimeOffset.UtcNow.AddMinutes(5)));

        var model = new EditorModel(mock.Object, reveals.Object)
        {
            Name = "sales.api",
            Secret = new EditorModel.SecretInputModel { Description = "desc" }
        };

        var result = await model.OnPostAddSecretAsync(cancellationToken: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
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

        var result = await model.OnPostAddSecretAsync(cancellationToken: CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ---------- OnPostDeleteAsync ----------

    [Fact]
    public async Task OnPostDeleteAsync_Success_RedirectsToIndexPage()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.DeleteAsync("sales.api", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        var model = new EditorModel(mock.Object) { Name = "sales.api" };

        var result = await model.OnPostDeleteAsync(confirmation: "DELETE", cancellationToken: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Index", redirect.PageName);
    }

    [Fact]
    public async Task OnPostDeleteAsync_ResourceNotFound_ReturnsNotFound()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.DeleteAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.NotFoundResult());

        var model = new EditorModel(mock.Object) { Name = "missing" };

        var result = await model.OnPostDeleteAsync(confirmation: "DELETE", cancellationToken: CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ---------- OnPostEnableAsync / OnPostDisableAsync ----------

    [Fact]
    public async Task OnPostDisableAsync_Success_RedirectsToBasicsTab()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.SetEnabledAsync("sales.api", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        var model = new EditorModel(mock.Object) { Name = "sales.api" };

        var result = await model.OnPostDisableAsync(confirmation: "DISABLE", cancellationToken: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("basics", redirect.RouteValues!["tab"]);
    }

    [Fact]
    public async Task OnPostDeleteAsync_InvalidConfirmation_RedirectsWithoutDeleting()
    {
        var mock = new Mock<IApiResourceEditorService>();
        var model = new EditorModel(mock.Object) { Name = "sales.api" };

        var result = await model.OnPostDeleteAsync(confirmation: "delete", cancellationToken: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("sales.api", redirect.RouteValues!["name"]);
        Assert.Contains("Type DELETE", model.ErrorMessage);
        mock.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnPostEnableAsync_ResourceNotFound_ReturnsNotFound()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.SetEnabledAsync("missing", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.NotFoundResult());

        var model = new EditorModel(mock.Object) { Name = "missing" };

        var result = await model.OnPostEnableAsync(cancellationToken: CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ---------- Scope & claim handlers ----------

    [Fact]
    public async Task OnPostAttachScopeAsync_Success_RedirectsToScopesTab()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.AttachScopeAsync("sales.api", "sales.read", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        var model = new EditorModel(mock.Object)
        {
            Name = "sales.api",
            AttachScope = new EditorModel.AttachScopeInputModel { ScopeName = "sales.read" }
        };

        var result = await model.OnPostAttachScopeAsync(cancellationToken: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("scopes", redirect.RouteValues!["tab"]);
    }

    [Fact]
    public async Task OnPostDetachScopeAsync_ScopeNotAttached_ReturnsNotFound()
    {
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.DetachScopeAsync("sales.api", "sales.read", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.NotFoundResult());

        var model = new EditorModel(mock.Object) { Name = "sales.api" };

        var result = await model.OnPostDetachScopeAsync("sales.read", cancellationToken: CancellationToken.None);

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

        var result = await model.OnPostAddClaimAsync(cancellationToken: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("claims", redirect.RouteValues!["tab"]);
    }

    [Fact]
    public async Task OnPostRemoveClaimAsync_ValidationFailure_ReloadsEditorAndRedisplaysClaimsTab()
    {
        var editor = MakeEditor("sales.api");
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.RemoveClaimAsync("sales.api", "sub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.ValidationFailure("Claim.ClaimType", "The sub claim cannot be removed."));
        mock.Setup(s => s.GetForEditAsync("sales.api", It.IsAny<CancellationToken>())).ReturnsAsync(editor);
        mock.Setup(s => s.GetAllApiScopeNamesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());

        var model = new EditorModel(mock.Object) { Name = "sales.api", Tab = "basics" };

        var result = await model.OnPostRemoveClaimAsync("sub", CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Same(editor, model.Editor);
        Assert.Equal("claims", model.Tab);
        Assert.Equal("The sub claim cannot be removed.", model.ModelState["Claim.ClaimType"]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task OnPostRevokeSecretAsync_ValidationFailure_ReloadsEditorAndRedisplaysSecretsTab()
    {
        var editor = MakeEditor("sales.api");
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.RevokeSecretAsync("sales.api", 17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.ValidationFailure(string.Empty, "The secret cannot be revoked."));
        mock.Setup(s => s.GetForEditAsync("sales.api", It.IsAny<CancellationToken>())).ReturnsAsync(editor);
        mock.Setup(s => s.GetAllApiScopeNamesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());

        var model = new EditorModel(mock.Object) { Name = "sales.api" };

        var result = await model.OnPostRevokeSecretAsync(17, "REVOKE", CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Same(editor, model.Editor);
        Assert.Equal("secrets", model.Tab);
        Assert.Equal("The secret cannot be revoked.", model.ModelState[string.Empty]!.Errors.Single().ErrorMessage);
    }

    // ---------- OnPostSaveBasicsAsync (invalid ModelState) ----------

    [Fact]
    public async Task OnPostSaveBasicsAsync_InvalidModelState_ReloadsEditorAndRedisplaysBasicsTabWithoutSaving()
    {
        var editor = MakeEditor("sales.api");
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.GetForEditAsync("sales.api", It.IsAny<CancellationToken>())).ReturnsAsync(editor);
        mock.Setup(s => s.SaveBasicsAsync(It.IsAny<SaveApiResourceBasicsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SaveApiResourceBasicsResult.ValidationFailure("Basics.Name", "The Name field is required."));

        var model = new EditorModel(mock.Object)
        {
            Name = "sales.api",
            Tab = "secrets",
            Basics = new EditorModel.BasicsInputModel { Name = string.Empty },
        };

        var result = await model.OnPostSaveBasicsAsync(cancellationToken: CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Same(editor, model.Editor);
        Assert.Equal("basics", model.Tab);
    }

    [Fact]
    public async Task OnPostSaveBasicsAsync_StronglyTypedValidationErrors_AddsFieldErrorsToModelState()
    {
        var editor = MakeEditor("sales.api");
        var validationErrors = new ApiResourceBasicsValidationErrors();
        validationErrors.AddNameError("Name is required.");
        validationErrors.AddDisplayNameError("Display name is invalid.");
        validationErrors.AddDescriptionError("Description is too long.");

        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.GetForEditAsync("sales.api", It.IsAny<CancellationToken>())).ReturnsAsync(editor);
        mock.Setup(s => s.SaveBasicsAsync(It.IsAny<SaveApiResourceBasicsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SaveApiResourceBasicsResult.ValidationFailure(validationErrors));

        var model = new EditorModel(mock.Object)
        {
            Name = "sales.api",
            Basics = new EditorModel.BasicsInputModel { Name = string.Empty },
        };

        var result = await model.OnPostSaveBasicsAsync(cancellationToken: CancellationToken.None);

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
        var editor = MakeEditor("sales.api");
        var mock = new Mock<IApiResourceEditorService>();
        mock.Setup(s => s.GetForEditAsync("sales.api", It.IsAny<CancellationToken>())).ReturnsAsync(editor);
        mock.Setup(s => s.GetAllApiScopeNamesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        mock.Setup(s => s.CreateScopeAsync(It.IsAny<CreateApiResourceScopeCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.ConflictResult("CreateScope.ScopeName", "A scope named 'taken.scope' already exists."));

        var model = new EditorModel(mock.Object)
        {
            Name = "sales.api",
            CreateScope = new EditorModel.CreateScopeInputModel { ScopeName = "taken.scope" }
        };

        var result = await model.OnPostCreateScopeAsync(cancellationToken: CancellationToken.None);

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

        var result = await model.OnPostCreateScopeAsync(cancellationToken: CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
