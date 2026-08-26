using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Pages.Admin.Clients;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientEditorPageModelCharacterisationTests
{
    [Fact]
    public async Task AuthenticationPost_ServiceValidationFailure_ReloadsClientDisplayAndAddsServiceErrors()
    {
        var service = new Mock<IClientDetailsService>();
        service.Setup(s => s.UpdateClientAuthenticationAsync(ClientId.Create("client-1"), It.IsAny<ClientAuthenticationInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.ValidationFailure("Input.RedirectUris", "The redirect URI is already assigned to another client."));
        service.Setup(s => s.GetClientAuthenticationAsync(ClientId.Create("client-1"), It.IsAny<CancellationToken>())).ReturnsAsync(new ClientAuthenticationModel
        {
            ClientId = ClientId.Create("client-1"),
            ClientName = "Client One",
            GrantTypes = new List<string> { "authorization_code" }
        });
        var model = new AuthenticationModel(service.Object)
        {
            Id = "client-1",
            Input = new AuthenticationInputModel
            {
                GrantTypes = new List<string> { "authorization_code" },
                RedirectUris = new List<string> { "https://example.test/signin" }
            }
        };

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Client One", model.ClientNameDisplay);
        Assert.Equal("The redirect URI is already assigned to another client.", model.ModelState["Input.RedirectUris"]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task TokenSettingsPost_ServiceValidationFailure_ReloadsClientDisplayAndSelectsConsentTab()
    {
        var service = new Mock<IClientDetailsService>();
        service.Setup(s => s.UpdateClientTokenSettingsAsync(ClientId.Create("client-1"), It.IsAny<ClientTokenSettingsInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.ValidationFailure("Input.AllowOfflineAccess", "Offline access is not permitted for this client."));
        service.Setup(s => s.GetClientTokenSettingsAsync(ClientId.Create("client-1"), It.IsAny<CancellationToken>())).ReturnsAsync(new ClientTokenSettingsModel
        {
            ClientId = ClientId.Create("client-1"),
            ClientName = "Client One"
        });
        var model = new TokenSettingsModel(service.Object)
        {
            Id = "client-1",
            Input = new TokenSettingsInputModel
            {
                AccessTokenLifetime = ValidationConstants.MinAccessTokenLifetime,
                IdentityTokenLifetime = ValidationConstants.MinIdentityTokenLifetime
            }
        };

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Client One", model.ClientNameDisplay);
        Assert.Equal(1, model.ActiveTabIndex);
        Assert.Equal("Offline access is not permitted for this client.", model.ModelState["Input.AllowOfflineAccess"]!.Errors.Single().ErrorMessage);
    }
}
