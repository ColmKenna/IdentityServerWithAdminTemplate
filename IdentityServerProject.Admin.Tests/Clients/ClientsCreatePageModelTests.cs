using IdentityServerProject.Admin.Tests.Infrastructure;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Duende.IdentityServer.Models;
using IdentityServerProject.Pages.Admin.Clients;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.SecretReveals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientsCreatePageModelTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ClientsCreatePageModelTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OnGetAsync_Should_InitializePresetDefaults_AndAvailableScopes()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            db.IdentityResources.Add(new IdentityResource(
                $"page-model-scope-{Guid.NewGuid():N}",
                new[] { "sub" }).ToEntity());
            await db.SaveChangesAsync();

            var createService = sp.GetRequiredService<IClientCreateService>();
            var presets = sp.GetRequiredService<IClientPresetService>();
            var revealService = sp.GetRequiredService<ISecretRevealService>();
            var pageModel = new CreateModel(createService, presets, revealService);

            await pageModel.OnGetAsync(null, CancellationToken.None);

            Assert.Equal("web", pageModel.Input.SelectedPreset);
            Assert.True(pageModel.Input.RequirePkce);
            Assert.True(pageModel.Input.RequireClientSecret);
            Assert.NotEmpty(pageModel.AvailableScopes);
        });
    }

    [Fact]
    public async Task OnPostAsync_Should_ReturnPageWithModelError_When_ModelStateIsInvalid()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var createService = sp.GetRequiredService<IClientCreateService>();
            var presets = sp.GetRequiredService<IClientPresetService>();
            var revealService = sp.GetRequiredService<ISecretRevealService>();
            var pageModel = new CreateModel(createService, presets, revealService);
            pageModel.ModelState.AddModelError("Input.ClientId", "Required");

            var result = await pageModel.OnPostAsync(CancellationToken.None);

            Assert.IsType<PageResult>(result);
            Assert.False(pageModel.ModelState.IsValid);
        });
    }

    [Fact]
    public async Task OnPostAsync_Should_SetTempData_AndRedirectToPage_When_CreationSucceeds()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var mockService = new Mock<IClientCreateService>();
            mockService
                .Setup(s => s.CreateClientAsync(It.IsAny<ClientCreateInputModel>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ClientCreateResult.Succeeded("new-web-client", "secret123"));

            var httpContext = new DefaultHttpContext();
            var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

            var presets = sp.GetRequiredService<IClientPresetService>();
            var revealService = new Mock<ISecretRevealService>();
            revealService.Setup(service => service.IssueAsync(
                    SecretRevealPurpose.ClientCreated,
                    "new-web-client",
                    "secret123",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SecretRevealTicket(SecretRevealHandle.Create("opaque-handle"), DateTimeOffset.UtcNow.AddMinutes(5)));

            var pageModel = new CreateModel(mockService.Object, presets, revealService.Object)
            {
                TempData = tempData,
                Input = new ClientCreateInputModel
                {
                    ClientId = "new-web-client",
                    ClientName = "New Web Client",
                    SelectedPreset = "web"
                }
            };

            var result = await pageModel.OnPostAsync(CancellationToken.None);

            var redirectResult = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("./Create", redirectResult.PageName);
            Assert.Equal("new-web-client", redirectResult.RouteValues!["clientId"]);
            Assert.False(redirectResult.RouteValues.ContainsKey("token"));
            Assert.Equal("opaque-handle", pageModel.SecretRevealHandle);
            Assert.DoesNotContain("secret123", pageModel.TempData.Values.OfType<string>());
        });
    }
}


