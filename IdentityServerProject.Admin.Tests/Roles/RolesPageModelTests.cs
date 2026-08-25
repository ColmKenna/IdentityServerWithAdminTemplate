using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Configuration;
using IdentityServerProject.Pages.Admin.Roles;
using IdentityServerProject.Services;
using IdentityServerProject.Services.Roles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Roles;

public class RolesPageModelTests
{
    private readonly Mock<IRoleService> _roleServiceMock = new();
    private readonly IOptions<AdminConsoleOptions> _options = Options.Create(new AdminConsoleOptions { DefaultPageSize = 10 });

    private static (PageContext PageContext, TempDataDictionary TempData) CreatePageContext()
    {
        var httpContext = new DefaultHttpContext();
        var modelState = new ModelStateDictionary();
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), modelState);
        var modelMetadataProvider = new EmptyModelMetadataProvider();
        var viewData = new ViewDataDictionary(modelMetadataProvider, modelState);
        var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        var pageContext = new PageContext(actionContext)
        {
            ViewData = viewData
        };

        return (pageContext, tempData);
    }

    [Fact]
    public async Task IndexModel_OnGetAsync_PopulatesRoles()
    {
        var expected = new ListResult<RoleListItem>
        {
            Items = new List<RoleListItem> { new() { Id = "1", Name = "Admin", IsProtected = true } },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10
        };

        _roleServiceMock.Setup(s => s.GetRolesAsync("test", Pagination.From(2, 10), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var (pageContext, tempData) = CreatePageContext();
        var model = new IndexModel(_roleServiceMock.Object, _options)
        {
            PageContext = pageContext,
            TempData = tempData,
            Filter = "test",
            PageNumber = 2
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Same(expected, model.Roles);
    }

    [Fact]
    public async Task IndexModel_OnPostDeleteAsync_Success_SetsStatusMessageAndRedirects()
    {
        _roleServiceMock.Setup(s => s.DeleteRoleAsync("role-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleDeleteResult.Succeeded());

        var (pageContext, tempData) = CreatePageContext();
        var model = new IndexModel(_roleServiceMock.Object, _options)
        {
            PageContext = pageContext,
            TempData = tempData,
            Filter = "role",
            PageNumber = 1
        };

        var result = await model.OnPostDeleteAsync("role-1", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName); // Redirects to same page with route values
        Assert.Equal("Role successfully deleted.", model.StatusMessage);
    }

    [Fact]
    public async Task IndexModel_OnPostDeleteAsync_Failure_SetsErrorMessageAndRedirects()
    {
        _roleServiceMock.Setup(s => s.DeleteRoleAsync("sysadmin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleDeleteResult.Failed("Cannot delete protected role."));

        var (pageContext, tempData) = CreatePageContext();
        var model = new IndexModel(_roleServiceMock.Object, _options)
        {
            PageContext = pageContext,
            TempData = tempData
        };

        var result = await model.OnPostDeleteAsync("sysadmin", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Cannot delete protected role.", model.ErrorMessage);
    }

    [Fact]
    public async Task CreateModel_OnPostAsync_InvalidModelState_ReturnsPage()
    {
        var (pageContext, tempData) = CreatePageContext();
        var model = new CreateModel(_roleServiceMock.Object)
        {
            PageContext = pageContext,
            TempData = tempData
        };
        model.ModelState.AddModelError("Input.Name", "Name is required.");

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        _roleServiceMock.Verify(s => s.CreateRoleAsync(It.IsAny<RoleCreateInputModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateModel_OnPostAsync_ServiceFailure_AddsModelErrorAndReturnsPage()
    {
        _roleServiceMock.Setup(s => s.CreateRoleAsync(It.IsAny<RoleCreateInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleCreateResult.Failed("Role already exists."));

        var (pageContext, tempData) = CreatePageContext();
        var model = new CreateModel(_roleServiceMock.Object)
        {
            PageContext = pageContext,
            TempData = tempData,
            Input = new CreateModel.InputModel { Name = "ExistingRole" }
        };

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(model.ModelState.ContainsKey(string.Empty));
        Assert.Equal("Role already exists.", model.ModelState[string.Empty]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task CreateModel_OnPostAsync_Success_SetsTempDataAndRedirectsToIndex()
    {
        _roleServiceMock.Setup(s => s.CreateRoleAsync(It.IsAny<RoleCreateInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleCreateResult.Succeeded("new-role-id"));

        var (pageContext, tempData) = CreatePageContext();
        var model = new CreateModel(_roleServiceMock.Object)
        {
            PageContext = pageContext,
            TempData = tempData,
            Input = new CreateModel.InputModel { Name = "SuperUser" }
        };

        var result = await model.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Index", redirect.PageName);
        Assert.Equal("Role 'SuperUser' was successfully created.", tempData["StatusMessage"]);
    }
}
