using IdentityServerProject.Pages.Admin.Clients;
using IdentityServerProject.Services.Clients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Moq;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientsDetailsPageModelTests
{
    private static ClientDetailsModel SampleClientDetails(string id = "coop.market.razor") => new()
    {
        ClientId = ClientId.Create(id),
        ClientName = "Co-op Market Razor Client",
        Description = "No description seeded or configured.",
        ClientType = "SPA with BFF",
        Enabled = true,
        RequirePkce = true,
        RequireClientSecret = true,
        RequireConsent = false,
        AllowOfflineAccess = true,
        AccessTokenLifetime = TokenLifetime.FromSeconds(300),
        AllowedGrantTypes = "authorization_code",
        RedirectUrisCount = 2,
        CorsOriginsCount = 0,
        SecretsCount = 1,
        AllowedScopesCount = 3,
        AllowedScopes = new List<string> { "openid", "profile", "coop.market.api" }
    };

    [Fact]
    public async Task OnGetAsync_ExistingClient_ReturnsPageResultAndSetsClientProperty()
    {
        var mockService = new Mock<IClientDetailsService>();
        mockService
            .Setup(s => s.GetClientDetailsAsync(ClientId.Create("coop.market.razor"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleClientDetails());

        var pageModel = new DetailsModel(mockService.Object);

        IActionResult result = await pageModel.OnGetAsync("coop.market.razor", CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.NotNull(pageModel.Client);
        Assert.Equal("coop.market.razor", pageModel.Client!.ClientId);
        Assert.Equal("Co-op Market Razor Client", pageModel.Client.ClientName);
    }

    [Fact]
    public async Task OnGetAsync_NonExistentClient_ReturnsNotFoundResult()
    {
        var mockService = new Mock<IClientDetailsService>();
        mockService
            .Setup(s => s.GetClientDetailsAsync(ClientId.Create("non-existent"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientDetailsModel?)null);

        var pageModel = new DetailsModel(mockService.Object);

        IActionResult result = await pageModel.OnGetAsync("non-existent", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPostToggleStatusAsync_ExistingClient_CallsServiceAndRedirectsToDetails()
    {
        var mockService = new Mock<IClientDetailsService>();
        mockService
            .Setup(s => s.ToggleClientStatusAsync(ClientId.Create("coop.market.razor"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var pageModel = new DetailsModel(mockService.Object);

        IActionResult result = await pageModel.OnPostToggleStatusAsync("coop.market.razor", CancellationToken.None);

        mockService.Verify(
            s => s.ToggleClientStatusAsync(ClientId.Create("coop.market.razor"), It.IsAny<CancellationToken>()),
            Times.Once);

        RedirectToPageResult redirectResult = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirectResult.PageName); // Redirects to current page
        Assert.Equal("coop.market.razor", redirectResult.RouteValues?["id"]);
    }

    [Fact]
    public async Task OnPostToggleStatusAsync_NonExistentClient_ReturnsNotFoundResult()
    {
        var mockService = new Mock<IClientDetailsService>();
        mockService
            .Setup(s => s.ToggleClientStatusAsync(ClientId.Create("non-existent"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var pageModel = new DetailsModel(mockService.Object);

        IActionResult result = await pageModel.OnPostToggleStatusAsync("non-existent", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ---------- OnPostDeleteAsync ----------

    [Fact]
    public async Task OnPostDeleteAsync_MissingId_ReturnsNotFoundWithoutCallingService()
    {
        var mockService = new Mock<IClientDetailsService>();

        var pageModel = new DetailsModel(mockService.Object);

        IActionResult result = await pageModel.OnPostDeleteAsync("  ", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        mockService.Verify(
            s => s.DeleteClientAsync(It.IsAny<ClientId>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnPostDeleteAsync_ClientNotFound_ReturnsNotFound()
    {
        var mockService = new Mock<IClientDetailsService>();
        mockService
            .Setup(s => s.DeleteClientAsync(ClientId.Create("coop.market.razor"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientDeleteResult.Failed("Client not found."));

        var pageModel = new DetailsModel(mockService.Object) { DeleteConfirmation = "DELETE" };

        IActionResult result = await pageModel.OnPostDeleteAsync("coop.market.razor", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        Assert.Null(pageModel.DeleteBlockedMessage);
    }

    [Fact]
    public async Task OnPostDeleteAsync_BlockedByRetentionRule_SetsMessageAndRedirectsToDetails()
    {
        const string blockReason =
            "Client has been disabled for 3 day(s). It can be deleted in 87 more day(s) (90-day retention rule).";
        var mockService = new Mock<IClientDetailsService>();
        mockService
            .Setup(s => s.DeleteClientAsync(ClientId.Create("coop.market.razor"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientDeleteResult.Failed(blockReason));

        var pageModel = new DetailsModel(mockService.Object) { DeleteConfirmation = "DELETE" };

        IActionResult result = await pageModel.OnPostDeleteAsync("coop.market.razor", CancellationToken.None);

        RedirectToPageResult redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName); // Redirects back to the current Details page
        Assert.Equal("coop.market.razor", redirect.RouteValues?["id"]);
        Assert.Equal(blockReason, pageModel.DeleteBlockedMessage);
    }

    [Fact]
    public async Task OnPostDeleteAsync_Success_RedirectsToIndex()
    {
        var mockService = new Mock<IClientDetailsService>();
        mockService
            .Setup(s => s.DeleteClientAsync(ClientId.Create("coop.market.razor"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientDeleteResult.Succeeded());

        var pageModel = new DetailsModel(mockService.Object) { DeleteConfirmation = " DELETE " };

        IActionResult result = await pageModel.OnPostDeleteAsync("coop.market.razor", CancellationToken.None);

        RedirectToPageResult redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Index", redirect.PageName);
        mockService.Verify(
            s => s.DeleteClientAsync(ClientId.Create("coop.market.razor"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("delete")]
    [InlineData("DELETE client")]
    public async Task OnPostDeleteAsync_InvalidTypedConfirmation_DoesNotCallService(string? confirmation)
    {
        var mockService = new Mock<IClientDetailsService>();
        var pageModel = new DetailsModel(mockService.Object) { DeleteConfirmation = confirmation };

        IActionResult result = await pageModel.OnPostDeleteAsync("coop.market.razor", CancellationToken.None);

        RedirectToPageResult redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("coop.market.razor", redirect.RouteValues?["id"]);
        Assert.Equal("Type DELETE exactly to confirm permanent deletion.", pageModel.DeleteBlockedMessage);
        mockService.Verify(
            s => s.DeleteClientAsync(It.IsAny<ClientId>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}