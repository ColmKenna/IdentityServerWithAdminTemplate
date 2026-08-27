using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Duende.IdentityServer.EntityFramework.Mappers;
using Duende.IdentityServer.Models;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.Extensions.DependencyInjection;
using Client = Duende.IdentityServer.Models.Client;
using Secret = Duende.IdentityServer.Models.Secret;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientAuthenticationServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ClientAuthenticationServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedClientAsync(Client client)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.Clients.Add(client.ToEntity());
            await configDb.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GetClientAuthenticationAsync_NonExistentClient_ReturnsNull()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ClientAuthenticationModel? result =
                await service.GetClientAuthenticationAsync(ClientId.Create("non-existent-client-id-xyz"));
            Assert.Null(result);
        });
    }

    [Fact]
    public async Task GetClientAuthenticationAsync_ExistingClient_ReturnsCurrentConfiguration()
    {
        string clientId = "auth-client-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "Authentication Test Client",
            RequirePkce = true,
            RequireClientSecret = true,
            AllowedGrantTypes = new List<string> { "authorization_code" },
            RedirectUris = new List<string> { "https://localhost:5001/signin-oidc" },
            AllowedCorsOrigins = new List<string> { "https://localhost:5001" }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ClientAuthenticationModel? result = await service.GetClientAuthenticationAsync(ClientId.Create(clientId));

            Assert.NotNull(result);
            Assert.True(result!.RequirePkce);
            Assert.True(result.RequireClientSecret);
            Assert.Equal(new[] { "authorization_code" }, result.GrantTypes);
            Assert.Equal(new[] { "https://localhost:5001/signin-oidc" }, result.RedirectUris);
            Assert.Equal(new[] { "https://localhost:5001" }, result.AllowedCorsOrigins);
            Assert.False(result.HasDrifted);
        });
    }

    [Fact]
    public async Task UpdateClientAuthenticationAsync_AddsAndRemovesRedirectUrisAndCorsOrigins()
    {
        string clientId = "auth-update-uris-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "URI Update Client",
            AllowedGrantTypes = new List<string> { "authorization_code" },
            RedirectUris = new List<string> { "https://old.example.com/callback" },
            AllowedCorsOrigins = new List<string>(),
            ClientSecrets = new List<Secret> { new("secret".Sha256()) }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();

            var input = new ClientAuthenticationInputModel
            {
                RequirePkce = true,
                RequireClientSecret = true,
                GrantTypes = new List<string> { "authorization_code" },
                RedirectUris = new List<string> { "https://new.example.com/callback" },
                CorsOrigins = new List<string> { "https://new.example.com" }
            };

            AdminMutationResult updateResult =
                await service.UpdateClientAuthenticationAsync(ClientId.Create(clientId), input);
            Assert.True(updateResult.Succeeded, updateResult.ErrorMessage);

            ClientAuthenticationModel? result = await service.GetClientAuthenticationAsync(ClientId.Create(clientId));
            Assert.NotNull(result);
            Assert.Equal(new[] { "https://new.example.com/callback" }, result!.RedirectUris);
            Assert.DoesNotContain("https://old.example.com/callback", result.RedirectUris);
            Assert.Equal(new[] { "https://new.example.com" }, result.AllowedCorsOrigins);
        });
    }

    [Fact]
    public async Task UpdateClientAuthenticationAsync_ChangesGrantTypes()
    {
        string clientId = "auth-update-grants-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "Grant Type Update Client",
            AllowedGrantTypes = new List<string> { "authorization_code" },
            ClientSecrets = new List<Secret> { new("secret".Sha256()) }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();

            var input = new ClientAuthenticationInputModel
            {
                RequirePkce = false,
                RequireClientSecret = true,
                GrantTypes = new List<string> { "client_credentials" },
                RedirectUris = new List<string>(),
                CorsOrigins = new List<string>()
            };

            AdminMutationResult updateResult =
                await service.UpdateClientAuthenticationAsync(ClientId.Create(clientId), input);
            Assert.True(updateResult.Succeeded, updateResult.ErrorMessage);

            ClientAuthenticationModel? result = await service.GetClientAuthenticationAsync(ClientId.Create(clientId));
            Assert.NotNull(result);
            Assert.Equal(new[] { "client_credentials" }, result!.GrantTypes);
        });
    }

    [Fact]
    public async Task UpdateClientAuthenticationAsync_NonExistentClient_ReturnsFalse()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            AdminMutationResult result = await service.UpdateClientAuthenticationAsync(
                ClientId.Create("unknown-client"), new ClientAuthenticationInputModel
                {
                    GrantTypes = new List<string> { "client_credentials" }
                });

            Assert.Equal(AdminMutationStatus.NotFound, result.Status);
        });
    }

    [Fact]
    public async Task GetClientAuthenticationAsync_NoPresetRecorded_HasNoDrift()
    {
        // Legacy/manually-created client with no admin:preset ClientProperty - nothing to drift from.
        string clientId = "auth-legacy-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "Legacy Client",
            RequirePkce = false,
            RequireClientSecret = false,
            AllowedGrantTypes = new List<string> { "implicit" }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ClientAuthenticationModel? result = await service.GetClientAuthenticationAsync(ClientId.Create(clientId));

            Assert.NotNull(result);
            Assert.False(result!.HasDrifted);
            Assert.Null(result.DriftDetails);
        });
    }

    [Fact]
    public async Task GetClientAuthenticationAsync_ConfigMatchesRecordedPreset_HasNoDrift()
    {
        string clientId = "auth-matching-preset-" + Guid.NewGuid().ToString("N");
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            Duende.IdentityServer.EntityFramework.Entities.Client entity = new Client
            {
                ClientId = clientId,
                ClientName = "Matching Preset Client",
                RequirePkce = true,
                RequireClientSecret = true,
                AllowedGrantTypes = new List<string> { "authorization_code" }
            }.ToEntity();
            entity.Properties.Add(new ClientProperty
            {
                Key = ClientCreateService.PresetPropertyKey,
                Value = "web"
            });
            configDb.Clients.Add(entity);
            await configDb.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ClientAuthenticationModel? result = await service.GetClientAuthenticationAsync(ClientId.Create(clientId));

            Assert.NotNull(result);
            Assert.False(result!.HasDrifted);
        });
    }

    [Fact]
    public async Task GetClientAuthenticationAsync_ConfigDivergesFromRecordedPreset_HasDrift()
    {
        string clientId = "auth-drifted-preset-" + Guid.NewGuid().ToString("N");
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            // Created as "web" preset (PKCE + secret required, authorization_code) but later
            // reconfigured to drop the client secret requirement - a real-world drift scenario.
            Duende.IdentityServer.EntityFramework.Entities.Client entity = new Client
            {
                ClientId = clientId,
                ClientName = "Drifted Preset Client",
                RequirePkce = true,
                RequireClientSecret = false,
                AllowedGrantTypes = new List<string> { "authorization_code" }
            }.ToEntity();
            entity.Properties.Add(new ClientProperty
            {
                Key = ClientCreateService.PresetPropertyKey,
                Value = "web"
            });
            configDb.Clients.Add(entity);
            await configDb.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ClientAuthenticationModel? result = await service.GetClientAuthenticationAsync(ClientId.Create(clientId));

            Assert.NotNull(result);
            Assert.True(result!.HasDrifted);
            Assert.Contains("Client secret", result.DriftDetails);
        });
    }
}