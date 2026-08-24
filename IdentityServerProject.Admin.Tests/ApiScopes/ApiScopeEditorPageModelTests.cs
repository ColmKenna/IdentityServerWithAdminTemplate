using IdentityServerProject.Admin.Tests.Infrastructure;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Pages.Admin.ApiScopes;
using IdentityServerProject.Services.ApiScopes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.ApiScopes;

/// <summary>
/// Unit tests for <see cref="EditModel"/> handler logic, exercised directly against a
/// mocked <see cref="IApiScopeEditorService"/> (no HTTP pipeline involved).
/// </summary>
public class ApiScopeEditorPageModelTests
{
    private static ApiScopeEditorModel MakeEditor(string name = "sales.scope") => new()
    {
        Name = name,
        DisplayName = "Sales Scope",
        Description = "Desc",
        Claims = new(),
    };

    // ---------- OnGetAsync ----------

    [Fact]
    public async Task OnGetAsync_NameResolvesToScope_PopulatesEditorAndInput()
    {
        var editor = MakeEditor();
        var mock = new Mock<IApiScopeEditorService>();
        mock.Setup(s => s.GetForEditAsync("sales.scope", It.IsAny<CancellationToken>())).ReturnsAsync(editor);

        var model = new EditModel(mock.Object) { Name = "sales.scope" };

        var result = await model.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Same(editor, model.Editor);
        Assert.Equal("Sales Scope", model.Input.DisplayName);
        Assert.Equal("Desc", model.Input.Description);
    }

    [Fact]
    public async Task OnGetAsync_NameDoesNotResolve_ReturnsNotFound()
    {
        var mock = new Mock<IApiScopeEditorService>();
        mock.Setup(s => s.GetForEditAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync(default(ApiScopeEditorModel));

        var model = new EditModel(mock.Object) { Name = "missing" };

        var result = await model.OnGetAsync(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnGetAsync_NoName_ReturnsNotFound()
    {
        var mock = new Mock<IApiScopeEditorService>();

        var model = new EditModel(mock.Object) { Name = string.Empty };

        var result = await model.OnGetAsync(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        mock.Verify(s => s.GetForEditAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- OnPostSaveAsync ----------

    [Fact]
    public async Task OnPostSaveAsync_Valid_UpdatesBasicsAndRedirectsToSameScope()
    {
        var mock = new Mock<IApiScopeEditorService>();
        mock.Setup(s => s.UpdateBasicsAsync("sales.scope", "New Display", "New Desc", true, false, false, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var model = new EditModel(mock.Object)
        {
            Name = "sales.scope",
            Input = new EditInputModel { DisplayName = "New Display", Description = "New Desc" },
        };

        var result = await model.OnPostSaveAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("sales.scope", redirect.RouteValues!["name"]);
        mock.Verify(s => s.UpdateBasicsAsync("sales.scope", "New Display", "New Desc", true, false, false, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnPostSaveAsync_ServiceReportsNotFound_ReturnsNotFound()
    {
        var mock = new Mock<IApiScopeEditorService>();
        mock.Setup(s => s.UpdateBasicsAsync("gone.scope", null, null, true, false, false, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var model = new EditModel(mock.Object) { Name = "gone.scope", Input = new EditInputModel() };

        var result = await model.OnPostSaveAsync(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPostSaveAsync_InvalidModelState_RedisplaysPageWithoutCallingService()
    {
        var editor = MakeEditor();
        var mock = new Mock<IApiScopeEditorService>();
        mock.Setup(s => s.GetForEditAsync("sales.scope", It.IsAny<CancellationToken>())).ReturnsAsync(editor);

        var model = new EditModel(mock.Object)
        {
            Name = "sales.scope",
            Input = new EditInputModel { DisplayName = new string('x', 500) },
        };
        model.ModelState.AddModelError("Input.DisplayName", "Too long");

        var result = await model.OnPostSaveAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Same(editor, model.Editor);
        mock.Verify(s => s.UpdateBasicsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- OnPostAddClaimAsync ----------

    [Fact]
    public async Task OnPostAddClaimAsync_Success_RedirectsToSameScope()
    {
        var mock = new Mock<IApiScopeEditorService>();
        mock.Setup(s => s.AddClaimAsync("sales.scope", "email", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var model = new EditModel(mock.Object);

        var result = await model.OnPostAddClaimAsync("sales.scope", "email", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("sales.scope", redirect.RouteValues!["name"]);
    }

    [Fact]
    public async Task OnPostAddClaimAsync_ScopeNotFound_ReturnsNotFound()
    {
        var mock = new Mock<IApiScopeEditorService>();
        mock.Setup(s => s.AddClaimAsync("missing", "email", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var model = new EditModel(mock.Object);

        var result = await model.OnPostAddClaimAsync("missing", "email", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ---------- OnPostRemoveClaimAsync ----------

    [Fact]
    public async Task OnPostRemoveClaimAsync_Success_RedirectsToSameScope()
    {
        var mock = new Mock<IApiScopeEditorService>();
        mock.Setup(s => s.RemoveClaimAsync("sales.scope", "email", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var model = new EditModel(mock.Object);

        var result = await model.OnPostRemoveClaimAsync("sales.scope", "email", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("sales.scope", redirect.RouteValues!["name"]);
    }

    [Fact]
    public async Task OnPostRemoveClaimAsync_ScopeOrClaimNotFound_ReturnsNotFound()
    {
        var mock = new Mock<IApiScopeEditorService>();
        mock.Setup(s => s.RemoveClaimAsync("sales.scope", "missing-claim", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var model = new EditModel(mock.Object);

        var result = await model.OnPostRemoveClaimAsync("sales.scope", "missing-claim", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}


