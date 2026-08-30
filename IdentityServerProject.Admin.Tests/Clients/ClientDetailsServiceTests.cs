using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Duende.IdentityServer.EntityFramework.Mappers;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Client = Duende.IdentityServer.Models.Client;
using Secret = Duende.IdentityServer.Models.Secret;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientDetailsServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ClientDetailsServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static Client MakeClient(
        string clientId,
        string clientName,
        string description = "Test description",
        bool enabled = true,
        bool requirePkce = true,
        bool requireClientSecret = true,
        bool requireConsent = false,
        bool allowOfflineAccess = true,
        int accessTokenLifetime = 300)
    {
        return new Client
        {
            ClientId = clientId,
            ClientName = clientName,
            Description = description,
            Enabled = enabled,
            RequirePkce = requirePkce,
            RequireClientSecret = requireClientSecret,
            RequireConsent = requireConsent,
            AllowOfflineAccess = allowOfflineAccess,
            AccessTokenLifetime = accessTokenLifetime,
            AllowedGrantTypes = new List<string> { "authorization_code" },
            RedirectUris = new List<string> { "https://localhost:5001/signin-oidc", "https://localhost:5001/callback" },
            AllowedCorsOrigins = new List<string>(),
            ClientSecrets = new List<Secret> { new("hashed_secret") },
            AllowedScopes = new List<string> { "openid", "profile", "coop.market.api" }
        };
    }

    private async Task SeedAsync(params Client[] clients)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            foreach (Client client in clients) configDb.Clients.Add(client.ToEntity());
            await configDb.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GetClientDetailsAsync_NonExistentClient_ReturnsNull()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IClientOverviewService service = sp.GetRequiredService<IClientOverviewService>();
            ClientDetailsModel? result =
                await service.GetClientDetailsAsync(ClientId.Create("non-existent-client-id-xyz"));
            Assert.Null(result);
        });
    }

    [Fact]
    public async Task GetClientDetailsAsync_ExistingClient_ReturnsMappedDetails()
    {
        string clientId = "coop.market.razor." + Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(clientId, "Co-op Market Razor Client"));

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientOverviewService service = sp.GetRequiredService<IClientOverviewService>();

            ClientDetailsModel? details = await service.GetClientDetailsAsync(ClientId.Create(clientId));

            Assert.NotNull(details);
            Assert.Equal(clientId, details!.ClientId);
            Assert.Equal("Co-op Market Razor Client", details.ClientName);
            Assert.Equal("Test description", details.Description);
            Assert.True(details.Enabled);
            Assert.True(details.RequirePkce);
            Assert.True(details.RequireClientSecret);
            Assert.False(details.RequireConsent);
            Assert.True(details.AllowOfflineAccess);
            Assert.Equal(300, details.AccessTokenLifetime.Seconds);
            Assert.Equal("authorization_code", details.AllowedGrantTypes);
            Assert.Equal(2, details.RedirectUrisCount);
            Assert.Equal(0, details.CorsOriginsCount);
            Assert.Equal(1, details.SecretsCount);
            Assert.Equal(3, details.AllowedScopesCount);
            Assert.Equal(new[] { "openid", "profile", "coop.market.api" }, details.AllowedScopes);
        });
    }

    [Fact]
    public async Task ToggleClientStatusAsync_ExistingClient_TogglesEnabledFlag()
    {
        string clientId = "toggle-client-" + Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(clientId, "Toggle Client", enabled: true));

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientOverviewService service = sp.GetRequiredService<IClientOverviewService>();

            bool firstToggle = await service.ToggleClientStatusAsync(ClientId.Create(clientId));
            Assert.True(firstToggle);

            ClientDetailsModel? detailsAfterFirst = await service.GetClientDetailsAsync(ClientId.Create(clientId));
            Assert.NotNull(detailsAfterFirst);
            Assert.False(detailsAfterFirst!.Enabled);

            bool secondToggle = await service.ToggleClientStatusAsync(ClientId.Create(clientId));
            Assert.True(secondToggle);

            ClientDetailsModel? detailsAfterSecond = await service.GetClientDetailsAsync(ClientId.Create(clientId));
            Assert.NotNull(detailsAfterSecond);
            Assert.True(detailsAfterSecond!.Enabled);
        });
    }

    [Fact]
    public async Task ToggleClientStatusAsync_NonExistentClient_ReturnsFalse()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IClientOverviewService service = sp.GetRequiredService<IClientOverviewService>();
            bool result = await service.ToggleClientStatusAsync(ClientId.Create("non-existent-client-id-abc"));
            Assert.False(result);
        });
    }

    [Fact]
    public async Task UpdateClientBasicsAsync_ExistingClient_UpdatesNameAndDescription()
    {
        string clientId = "basics-client-" + Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(clientId, "Original Name", "Original Description"));

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientOverviewService service = sp.GetRequiredService<IClientOverviewService>();

            AdminMutationResult result =
                await service.UpdateClientBasicsAsync(ClientId.Create(clientId), "New Name", "New Description");
            Assert.True(result.Succeeded, result.ErrorMessage);

            ClientDetailsModel? details = await service.GetClientDetailsAsync(ClientId.Create(clientId));
            Assert.NotNull(details);
            Assert.Equal("New Name", details!.ClientName);
            Assert.Equal("New Description", details.Description);
        });
    }

    [Fact]
    public async Task UpdateClientBasicsAsync_NonExistentClient_ReturnsFalse()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IClientOverviewService service = sp.GetRequiredService<IClientOverviewService>();
            AdminMutationResult result =
                await service.UpdateClientBasicsAsync(ClientId.Create("non-existent-client-id-abc"), "New Name",
                    "New Description");
            Assert.Equal(AdminMutationStatus.NotFound, result.Status);
        });
    }

    [Fact]
    public async Task DeleteClientAsync_EnabledClient_IsBlocked()
    {
        string clientId = "delete-enabled-" + Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(clientId, "Enabled Client", enabled: true));

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientOverviewService service = sp.GetRequiredService<IClientOverviewService>();

            ClientDeleteResult result = await service.DeleteClientAsync(ClientId.Create(clientId));

            Assert.False(result.Success);
            Assert.Equal("Client must be disabled before it can be deleted.", result.ErrorMessage);

            ClientDetailsModel? stillExists = await service.GetClientDetailsAsync(ClientId.Create(clientId));
            Assert.NotNull(stillExists);
        });
    }

    [Fact]
    public async Task DeleteClientAsync_DisabledLessThan90Days_IsBlocked()
    {
        string clientId = "delete-recent-" + Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(clientId, "Recently Disabled Client", enabled: true));

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientOverviewService service = sp.GetRequiredService<IClientOverviewService>();

            // Disable via the service so admin:disabledAt is stamped with "now".
            await service.ToggleClientStatusAsync(ClientId.Create(clientId));

            ClientDetailsModel? details = await service.GetClientDetailsAsync(ClientId.Create(clientId));
            Assert.NotNull(details);
            Assert.False(details!.Enabled);
            Assert.False(details.CanDelete);

            ClientDeleteResult result = await service.DeleteClientAsync(ClientId.Create(clientId));

            Assert.False(result.Success);
            Assert.Contains("90-day retention rule", result.ErrorMessage);

            ClientDetailsModel? stillExists = await service.GetClientDetailsAsync(ClientId.Create(clientId));
            Assert.NotNull(stillExists);
        });
    }

    [Fact]
    public async Task DeleteClientAsync_DisabledAtLeast90Days_Succeeds()
    {
        string clientId = "delete-eligible-" + Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(clientId, "Long Disabled Client", enabled: false));

        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            Duende.IdentityServer.EntityFramework.Entities.Client entity =
                await configDb.Clients.SingleAsync(c => c.ClientId == clientId);
            configDb.Set<ClientProperty>().Add(new ClientProperty
            {
                ClientId = entity.Id,
                Key = "admin:disabledAt",
                Value = DateTime.UtcNow.AddDays(-91).ToString("O")
            });
            await configDb.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientOverviewService service = sp.GetRequiredService<IClientOverviewService>();

            ClientDetailsModel? details = await service.GetClientDetailsAsync(ClientId.Create(clientId));
            Assert.NotNull(details);
            Assert.True(details!.CanDelete);

            ClientDeleteResult result = await service.DeleteClientAsync(ClientId.Create(clientId));
            Assert.True(result.Success);

            ClientDetailsModel? afterDelete = await service.GetClientDetailsAsync(ClientId.Create(clientId));
            Assert.Null(afterDelete);
        });
    }

    [Fact]
    public async Task DeleteClientAsync_NonExistentClient_ReturnsFailure()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IClientOverviewService service = sp.GetRequiredService<IClientOverviewService>();
            ClientDeleteResult result = await service.DeleteClientAsync(ClientId.Create("non-existent-client-id-xyz"));
            Assert.False(result.Success);
        });
    }

    [Fact]
    public async Task ToggleClientStatusAsync_DisableThenEnable_ClearsDisabledAtTracking()
    {
        string clientId = "toggle-disabledat-" + Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(clientId, "Toggle Tracking Client", enabled: true));

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientOverviewService service = sp.GetRequiredService<IClientOverviewService>();
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();

            await service.ToggleClientStatusAsync(ClientId.Create(clientId));

            Duende.IdentityServer.EntityFramework.Entities.Client entity = await configDb.Clients
                .Include(c => c.Properties)
                .SingleAsync(c => c.ClientId == clientId);
            Assert.Contains(entity.Properties, p => p.Key == "admin:disabledAt");

            await service.ToggleClientStatusAsync(ClientId.Create(clientId));

            Duende.IdentityServer.EntityFramework.Entities.Client entityAfterReEnable = await configDb.Clients
                .Include(c => c.Properties)
                .SingleAsync(c => c.ClientId == clientId);
            Assert.DoesNotContain(entityAfterReEnable.Properties, p => p.Key == "admin:disabledAt");
        });
    }
}