using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Duende.IdentityServer.EntityFramework.Mappers;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Duende.IdentityServer.Models.HashExtensions;
using ClientModel = Duende.IdentityServer.Models.Client;
using ApiScopeModel = Duende.IdentityServer.Models.ApiScope;
using IdentityResourceModel = Duende.IdentityServer.Models.IdentityResource;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientPermissionsServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ClientPermissionsServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedScopesAsync(string tag)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();

            if (!await configDb.IdentityResources.AnyAsync(r => r.Name == "openid"))
            {
                configDb.IdentityResources.Add(new IdentityResourceModel("openid", new List<string> { "sub" }).ToEntity());
                configDb.IdentityResources.Add(new IdentityResourceModel("profile", new List<string> { "name" }).ToEntity());
            }

            configDb.ApiScopes.Add(new ApiScopeModel($"{tag}.read", "Read Access").ToEntity());
            configDb.ApiScopes.Add(new ApiScopeModel($"{tag}.write", "Write Access").ToEntity());
            await configDb.SaveChangesAsync();
        });
    }

    private async Task SeedClientAsync(ClientModel client)
    {
        var entity = client.ToEntity();
        var isM2M = client.AllowedGrantTypes.Contains("client_credentials");

        if (isM2M)
        {
            entity.RequireClientSecret = true;
            entity.RequirePkce = false;
            entity.ClientSecrets = new List<ClientSecret>
            {
                new ClientSecret { Value = "secret".Sha256(), Type = "SharedSecret" }
            };
        }
        else
        {
            entity.RequireClientSecret = false;
            entity.RequirePkce = true;
            if (entity.AllowedGrantTypes.Any(g => g.GrantType == "authorization_code") && (entity.RedirectUris == null || entity.RedirectUris.Count == 0))
            {
                entity.RedirectUris = new List<ClientRedirectUri> { new ClientRedirectUri { RedirectUri = "https://example.com/callback" } };
            }
        }

        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.Clients.Add(entity);
            await configDb.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GetClientPermissionsAsync_NonExistentClient_ReturnsNull()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.GetClientPermissionsAsync(ClientId.Create("non-existent-client-id-xyz"));
            Assert.Null(result);
        });
    }

    [Fact]
    public async Task GetClientPermissionsAsync_ExistingClient_ReturnsAllowedAndAvailableScopes()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedScopesAsync(tag);

        var clientId = $"{tag}-get-permissions";
        await SeedClientAsync(new ClientModel
        {
            ClientId = clientId,
            ClientName = "Get Permissions Client",
            AllowedGrantTypes = new List<string> { "authorization_code" },
            AllowedScopes = new List<string> { "openid", $"{tag}.read" }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.GetClientPermissionsAsync(ClientId.Create(clientId));

            Assert.NotNull(result);
            Assert.Equal(clientId, result!.ClientId);
            Assert.True(result.IsInteractive);
            Assert.Contains("openid", result.AllowedScopes);
            Assert.Contains($"{tag}.read", result.AllowedScopes);
            Assert.Contains($"{tag}.write", result.AvailableApiScopes);
        });
    }

    [Fact]
    public async Task GetClientPermissionsAsync_M2MClient_SetsIsInteractiveToFalse()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedScopesAsync(tag);

        var clientId = $"{tag}-m2m-permissions";
        await SeedClientAsync(new ClientModel
        {
            ClientId = clientId,
            ClientName = "M2M Client",
            AllowedGrantTypes = new List<string> { "client_credentials" },
            AllowedScopes = new List<string> { $"{tag}.read" }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.GetClientPermissionsAsync(ClientId.Create(clientId));

            Assert.NotNull(result);
            Assert.False(result!.IsInteractive);
            Assert.Empty(result.AvailableIdentityScopes);
        });
    }

    [Fact]
    public async Task UpdateClientPermissionsAsync_InteractiveClient_EnforcesOpenIdEvenWhenOmitted()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedScopesAsync(tag);

        var clientId = $"{tag}-enforce-openid";
        await SeedClientAsync(new ClientModel
        {
            ClientId = clientId,
            ClientName = "Enforce OpenId Client",
            AllowedGrantTypes = new List<string> { "authorization_code" },
            AllowedScopes = new List<string> { "openid" }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();

            var updateResult = await service.UpdateClientPermissionsAsync(ClientId.Create(clientId), ScopeSet.FromStrings(new[] { $"{tag}.read" }));
            Assert.True(updateResult.Succeeded, updateResult.ErrorMessage);

            var result = await service.GetClientPermissionsAsync(ClientId.Create(clientId));
            Assert.NotNull(result);
            Assert.Contains("openid", result!.AllowedScopes);
            Assert.Contains($"{tag}.read", result.AllowedScopes);
        });
    }

    [Fact]
    public async Task UpdateClientPermissionsAsync_M2MClient_StripsIdentityScopesEvenIfSubmitted()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedScopesAsync(tag);

        var clientId = $"{tag}-strip-identity";
        await SeedClientAsync(new ClientModel
        {
            ClientId = clientId,
            ClientName = "Strip Identity Client",
            AllowedGrantTypes = new List<string> { "client_credentials" }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();

            var updateResult = await service.UpdateClientPermissionsAsync(ClientId.Create(clientId), ScopeSet.FromStrings(new[] { "openid", "profile", $"{tag}.write" }));
            Assert.True(updateResult.Succeeded, updateResult.ErrorMessage);

            var result = await service.GetClientPermissionsAsync(ClientId.Create(clientId));
            Assert.NotNull(result);
            Assert.DoesNotContain("openid", result!.AllowedScopes);
            Assert.DoesNotContain("profile", result.AllowedScopes);
            Assert.Contains($"{tag}.write", result.AllowedScopes);
        });
    }

    [Fact]
    public async Task UpdateClientPermissionsAsync_AddsAndRemovesApiScopes()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedScopesAsync(tag);

        var clientId = $"{tag}-add-remove-api";
        await SeedClientAsync(new ClientModel
        {
            ClientId = clientId,
            ClientName = "Add Remove API Client",
            AllowedGrantTypes = new List<string> { "authorization_code" },
            AllowedScopes = new List<string> { "openid", $"{tag}.read" }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();

            var updateResult = await service.UpdateClientPermissionsAsync(ClientId.Create(clientId), ScopeSet.FromStrings(new[] { "openid", $"{tag}.write" }));
            Assert.True(updateResult.Succeeded, updateResult.ErrorMessage);

            var result = await service.GetClientPermissionsAsync(ClientId.Create(clientId));
            Assert.NotNull(result);
            Assert.Contains($"{tag}.write", result!.AllowedScopes);
            Assert.DoesNotContain($"{tag}.read", result.AllowedScopes);
        });
    }

    [Fact]
    public async Task UpdateClientPermissionsAsync_NonExistentClient_ReturnsFalse()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.UpdateClientPermissionsAsync(ClientId.Create("non-existent-client-id-xyz"), ScopeSet.FromStrings(new[] { "openid" }));
            Assert.Equal(AdminMutationStatus.NotFound, result.Status);
        });
    }

    [Fact]
    public async Task UpdateClientPermissionsAsync_UnknownScope_IsFieldKeyedAndDoesNotMutate()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedScopesAsync(tag);
        var clientId = $"{tag}-unknown-scope";
        await SeedClientAsync(new ClientModel
        {
            ClientId = clientId,
            ClientName = "Unknown Scope Client",
            AllowedGrantTypes = new List<string> { "authorization_code" },
            AllowedScopes = new List<string> { "openid", $"{tag}.read" }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.UpdateClientPermissionsAsync(
                ClientId.Create(clientId),
                ScopeSet.FromStrings(new[] { "openid", "scope.that.is.not.configured" }));

            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
            Assert.True(result.Errors.ContainsKey("Input.AllowedScopes"));

            var persisted = await sp.GetRequiredService<ConfigurationDbContext>().Clients
                .AsNoTracking().Include(client => client.AllowedScopes)
                .SingleAsync(client => client.ClientId == clientId);
            Assert.Equal(
                new[] { "openid", $"{tag}.read" }.OrderBy(scope => scope),
                persisted.AllowedScopes.Select(scope => scope.Scope).OrderBy(scope => scope));
        });
    }
}
