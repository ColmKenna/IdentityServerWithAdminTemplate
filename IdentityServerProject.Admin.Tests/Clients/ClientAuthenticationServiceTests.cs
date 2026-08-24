using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Duende.IdentityServer.Models;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

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
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.Clients.Add(client.ToEntity());
            await configDb.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GetClientAuthenticationAsync_NonExistentClient_ReturnsNull()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.GetClientAuthenticationAsync("non-existent-client-id-xyz");
            Assert.Null(result);
        });
    }

    [Fact]
    public async Task GetClientAuthenticationAsync_ExistingClient_ReturnsCurrentConfiguration()
    {
        var clientId = "auth-client-" + Guid.NewGuid().ToString("N");
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
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.GetClientAuthenticationAsync(clientId);

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
        var clientId = "auth-update-uris-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "URI Update Client",
            AllowedGrantTypes = new List<string> { "authorization_code" },
            RedirectUris = new List<string> { "https://old.example.com/callback" },
            AllowedCorsOrigins = new List<string>(),
            ClientSecrets = new List<Duende.IdentityServer.Models.Secret> { new Duende.IdentityServer.Models.Secret("secret".Sha256()) }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();

            var input = new ClientAuthenticationInputModel
            {
                RequirePkce = true,
                RequireClientSecret = true,
                GrantTypes = new List<string> { "authorization_code" },
                RedirectUris = new List<string> { "https://new.example.com/callback" },
                CorsOrigins = new List<string> { "https://new.example.com" }
            };

            var updateResult = await service.UpdateClientAuthenticationAsync(clientId, input);
            Assert.True(updateResult.Succeeded, updateResult.ErrorMessage);

            var result = await service.GetClientAuthenticationAsync(clientId);
            Assert.NotNull(result);
            Assert.Equal(new[] { "https://new.example.com/callback" }, result!.RedirectUris);
            Assert.DoesNotContain("https://old.example.com/callback", result.RedirectUris);
            Assert.Equal(new[] { "https://new.example.com" }, result.AllowedCorsOrigins);
        });
    }

    [Fact]
    public async Task UpdateClientAuthenticationAsync_ChangesGrantTypes()
    {
        var clientId = "auth-update-grants-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "Grant Type Update Client",
            AllowedGrantTypes = new List<string> { "authorization_code" },
            ClientSecrets = new List<Duende.IdentityServer.Models.Secret> { new Duende.IdentityServer.Models.Secret("secret".Sha256()) }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();

            var input = new ClientAuthenticationInputModel
            {
                RequirePkce = false,
                RequireClientSecret = true,
                GrantTypes = new List<string> { "client_credentials" },
                RedirectUris = new List<string>(),
                CorsOrigins = new List<string>()
            };

            var updateResult = await service.UpdateClientAuthenticationAsync(clientId, input);
            Assert.True(updateResult.Succeeded, updateResult.ErrorMessage);

            var result = await service.GetClientAuthenticationAsync(clientId);
            Assert.NotNull(result);
            Assert.Equal(new[] { "client_credentials" }, result!.GrantTypes);
        });
    }

    [Fact]
    public async Task UpdateClientAuthenticationAsync_NonExistentClient_ReturnsFalse()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.UpdateClientAuthenticationAsync("unknown-client", new ClientAuthenticationInputModel
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
        var clientId = "auth-legacy-" + Guid.NewGuid().ToString("N");
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
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.GetClientAuthenticationAsync(clientId);

            Assert.NotNull(result);
            Assert.False(result!.HasDrifted);
            Assert.Null(result.DriftDetails);
        });
    }

    [Fact]
    public async Task GetClientAuthenticationAsync_ConfigMatchesRecordedPreset_HasNoDrift()
    {
        var clientId = "auth-matching-preset-" + Guid.NewGuid().ToString("N");
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var entity = new Client
            {
                ClientId = clientId,
                ClientName = "Matching Preset Client",
                RequirePkce = true,
                RequireClientSecret = true,
                AllowedGrantTypes = new List<string> { "authorization_code" }
            }.ToEntity();
            entity.Properties.Add(new Duende.IdentityServer.EntityFramework.Entities.ClientProperty
            {
                Key = ClientCreateService.PresetPropertyKey,
                Value = "web"
            });
            configDb.Clients.Add(entity);
            await configDb.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.GetClientAuthenticationAsync(clientId);

            Assert.NotNull(result);
            Assert.False(result!.HasDrifted);
        });
    }

    [Fact]
    public async Task GetClientAuthenticationAsync_ConfigDivergesFromRecordedPreset_HasDrift()
    {
        var clientId = "auth-drifted-preset-" + Guid.NewGuid().ToString("N");
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            // Created as "web" preset (PKCE + secret required, authorization_code) but later
            // reconfigured to drop the client secret requirement - a real-world drift scenario.
            var entity = new Client
            {
                ClientId = clientId,
                ClientName = "Drifted Preset Client",
                RequirePkce = true,
                RequireClientSecret = false,
                AllowedGrantTypes = new List<string> { "authorization_code" }
            }.ToEntity();
            entity.Properties.Add(new Duende.IdentityServer.EntityFramework.Entities.ClientProperty
            {
                Key = ClientCreateService.PresetPropertyKey,
                Value = "web"
            });
            configDb.Clients.Add(entity);
            await configDb.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.GetClientAuthenticationAsync(clientId);

            Assert.NotNull(result);
            Assert.True(result!.HasDrifted);
            Assert.Contains("Client secret", result.DriftDetails);
        });
    }
}
