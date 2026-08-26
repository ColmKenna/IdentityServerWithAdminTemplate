using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Pages.Admin.Clients;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.SecretReveals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientsClonePageModelTests
{
    private static ClientDetailsModel SourceClient() => new()
    {
        ClientId = ClientId.Create("source-client"),
        ClientName = "Source Client",
        Description = "Source description",
        ClientType = "Web",
        AllowedGrantTypes = "authorization_code"
    };

    private static CloneModel CreateModel(
        Mock<IClientCreateService> createService,
        Mock<IClientDetailsService> detailsService,
        Mock<ISecretRevealService>? revealService = null)
    {
        var httpContext = new DefaultHttpContext();
        var modelState = new ModelStateDictionary();
        var pageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), modelState))
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState)
        };

        return new CloneModel(createService.Object, detailsService.Object, (revealService ?? new Mock<ISecretRevealService>()).Object)
        {
            PageContext = pageContext,
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };
    }

    [Fact]
    public async Task OnGetAsync_ExistingSource_PopulatesCloneDefaults()
    {
        var createService = new Mock<IClientCreateService>();
        var detailsService = new Mock<IClientDetailsService>();
        detailsService.Setup(s => s.GetClientDetailsAsync(ClientId.Create("source-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SourceClient());
        var model = CreateModel(createService, detailsService);
        model.SourceClientId = "source-client";

        var result = await model.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Source Client", model.SourceClientName);
        Assert.Equal("source-client-clone", model.Input.ClientId);
        Assert.Equal("Source Client (Clone)", model.Input.ClientName);
        Assert.Equal("Source description", model.Input.Description);
    }

    [Fact]
    public async Task OnPostAsync_SourceNotFound_ReturnsNotFoundWithoutCloning()
    {
        var createService = new Mock<IClientCreateService>();
        var detailsService = new Mock<IClientDetailsService>();
        detailsService.Setup(s => s.GetClientDetailsAsync(ClientId.Create("missing"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientDetailsModel?)null);
        var model = CreateModel(createService, detailsService);
        model.SourceClientId = "missing";

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        createService.Verify(s => s.CloneClientAsync(It.IsAny<string>(), It.IsAny<ClientCreateInputModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnPostAsync_ValidationFailure_MapsUnprefixedFieldErrorAndRedisplaysPage()
    {
        var createService = new Mock<IClientCreateService>();
        createService.Setup(s => s.CloneClientAsync("source-client", It.IsAny<ClientCreateInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCreateResult.Failed("ClientId", "A client with this ID already exists."));
        var detailsService = new Mock<IClientDetailsService>();
        detailsService.Setup(s => s.GetClientDetailsAsync(ClientId.Create("source-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SourceClient());
        var model = CreateModel(createService, detailsService);
        model.SourceClientId = "source-client";
        model.Input = new ClientCreateInputModel { ClientId = "target-client", ClientName = "Target Client" };

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Source Client", model.SourceClientName);
        Assert.Equal("A client with this ID already exists.", model.ModelState["Input.ClientId"]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task OnPostAsync_SuccessWithSecret_StoresOnlyRevealHandleAndRedirectsToCreate()
    {
        var createService = new Mock<IClientCreateService>();
        createService.Setup(s => s.CloneClientAsync("source-client", It.IsAny<ClientCreateInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCreateResult.Succeeded("target-client", "plaintext-secret"));
        var detailsService = new Mock<IClientDetailsService>();
        detailsService.Setup(s => s.GetClientDetailsAsync(ClientId.Create("source-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SourceClient());
        var revealService = new Mock<ISecretRevealService>();
        revealService.Setup(s => s.IssueAsync(new SecretRevealTarget(SecretRevealPurpose.ClientCreated, "target-client"), "plaintext-secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretRevealTicket(SecretRevealHandle.Create("opaque-handle"), DateTimeOffset.UtcNow.AddMinutes(5)));
        var model = CreateModel(createService, detailsService, revealService);
        model.SourceClientId = "source-client";
        model.Input = new ClientCreateInputModel { ClientId = "target-client", ClientName = "Target Client" };

        var result = await model.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Create", redirect.PageName);
        Assert.Equal("target-client", redirect.RouteValues!["clientId"]);
        Assert.Equal("opaque-handle", ((SecretRevealHandle)model.TempData["SecretRevealHandle"]!).Value);
        Assert.DoesNotContain("plaintext-secret", model.TempData.Values.OfType<string>());
    }
}
