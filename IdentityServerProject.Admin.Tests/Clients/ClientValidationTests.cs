using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Duende.IdentityServer.Models;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientValidationTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ClientValidationTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("https://example.com/path")]
    [InlineData("https://example.com?query=value")]
    [InlineData("https://example.com#fragment")]
    [InlineData("https://user@example.com")]
    [InlineData("ftp://example.com")]
    public void TryNormalizeCorsOrigin_NonOriginValues_AreRejected(string value)
    {
        Assert.False(UriValidationHelper.TryNormalizeCorsOrigin(
            value, ValidationConstants.MaxClientCorsOriginLength, out _));
    }

    [Fact]
    public void TryNormalizeCorsOrigin_RootTrailingSlash_IsRemoved()
    {
        Assert.True(UriValidationHelper.TryNormalizeCorsOrigin(
            "https://Example.com:8443/", ValidationConstants.MaxClientCorsOriginLength, out var normalized));
        Assert.Equal("https://example.com:8443", normalized);
    }

    [Fact]
    public async Task CreateClientAsync_CorsOrigins_AreNormalizedAndDeduplicated()
    {
        var clientId = $"cors-create-{Guid.NewGuid():N}";
        await _factory.RunInScopeAsync(async sp =>
        {
            var configurationDb = sp.GetRequiredService<ConfigurationDbContext>();
            if (!await configurationDb.IdentityResources.AnyAsync(resource => resource.Name == "openid"))
            {
                configurationDb.IdentityResources.Add(new Duende.IdentityServer.EntityFramework.Entities.IdentityResource
                {
                    Name = "openid",
                    DisplayName = "OpenID"
                });
                await configurationDb.SaveChangesAsync();
            }

            var service = sp.GetRequiredService<IClientCreateService>();
            var result = await service.CreateClientAsync(new ClientCreateInputModel
            {
                ClientId = clientId,
                ClientName = "CORS Normalization Client",
                SelectedPreset = "spa-nobff",
                RequireClientSecret = false,
                GrantTypes = new List<string> { "authorization_code" },
                RedirectUris = new List<string> { "https://example.com/callback" },
                CorsOrigins = new List<string> { "https://EXAMPLE.com/", "https://example.com" }
            });
            Assert.True(result.Success, result.ErrorMessage);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var client = await sp.GetRequiredService<ConfigurationDbContext>().Clients
                .AsNoTracking()
                .Include(entity => entity.AllowedCorsOrigins)
                .SingleAsync(entity => entity.ClientId == clientId);
            Assert.Equal("https://example.com", Assert.Single(client.AllowedCorsOrigins).Origin);
        });
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/callback")]
    [InlineData("/relative/path")]
    [InlineData("not-a-url")]
    public async Task CreateClientAsync_InvalidRedirectUri_FailsValidation(string invalidUri)
    {
        var tag = Guid.NewGuid().ToString("N");
        var clientId = $"{tag}-client";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientCreateService>();

            var input = new ClientCreateInputModel
            {
                ClientId = clientId,
                ClientName = "Test Client",
                RedirectUris = new List<string> { invalidUri }
            };

            var result = await service.CreateClientAsync(input);

            Assert.False(result.Success);
            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
            Assert.True(result.Errors.ContainsKey("RedirectUris"));
        });
    }

    [Fact]
    public async Task CreateClientAsync_UnknownAllowedScope_FailsValidation()
    {
        var tag = Guid.NewGuid().ToString("N");
        var clientId = $"{tag}-client";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientCreateService>();

            var input = new ClientCreateInputModel
            {
                ClientId = clientId,
                ClientName = "Test Client",
                RedirectUris = new List<string> { "https://example.com/callback" },
                AllowedScopes = new List<string> { "unknown.nonexistent.scope" }
            };

            var result = await service.CreateClientAsync(input);

            Assert.False(result.Success);
            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
            Assert.True(result.Errors.ContainsKey("AllowedScopes"));
        });
    }

    [Fact]
    public async Task CreateClientAsync_OverlongCollectionValues_ReturnsFieldErrorsAndDoesNotMutate()
    {
        var clientId = $"length-create-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientCreateService>();
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            var input = new ClientCreateInputModel
            {
                ClientId = clientId,
                ClientName = "Length Validation Client",
                GrantTypes = new List<string> { new('g', ValidationConstants.MaxGrantTypeLength + 1) },
                RedirectUris = new List<string> { "https://example.com/" + new string('r', ValidationConstants.MaxClientRedirectUriLength) },
                PostLogoutRedirectUris = new List<string> { "https://example.com/" + new string('p', ValidationConstants.MaxClientPostLogoutRedirectUriLength) },
                CorsOrigins = new List<string> { "https://example.com/" + new string('c', ValidationConstants.MaxClientCorsOriginLength) }
            };

            var result = await service.CreateClientAsync(input);

            Assert.False(result.Success);
            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
            Assert.Contains("GrantTypes", result.Errors.Keys);
            Assert.Contains("RedirectUris", result.Errors.Keys);
            Assert.Contains("PostLogoutRedirectUris", result.Errors.Keys);
            Assert.Contains("CorsOrigins", result.Errors.Keys);
            Assert.False(await db.Clients.AsNoTracking().AnyAsync(client => client.ClientId == clientId));
        });
    }

    [Fact]
    public async Task UpdateClientAuthenticationAsync_InvalidCorsOrigin_FailsValidation()
    {
        var tag = Guid.NewGuid().ToString("N");
        var clientId = $"{tag}-client";

        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            db.Clients.Add(new Client
            {
                ClientId = clientId,
                ClientName = "Test Client",
                RequireClientSecret = false,
                RequirePkce = true,
                AllowedGrantTypes = new List<string> { "authorization_code" },
                RedirectUris = new List<string> { "https://example.com/callback" }
            }.ToEntity());
            await db.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var detailsService = sp.GetRequiredService<IClientDetailsService>();

            var input = new ClientAuthenticationInputModel
            {
                GrantTypes = new List<string> { "authorization_code" },
                RedirectUris = new List<string> { "https://example.com/callback" },
                CorsOrigins = new List<string> { "invalid-origin" }
            };

            var result = await detailsService.UpdateClientAuthenticationAsync(ClientId.Create(clientId), input);

            Assert.False(result.Succeeded);
            Assert.NotNull(result.ErrorMessage);
        });
    }

    [Fact]
    public async Task UpdateClientAuthenticationAsync_CorsOrigins_AreNormalizedAndDeduplicated()
    {
        var clientId = $"cors-edit-{Guid.NewGuid():N}";
        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            db.Clients.Add(new Client
            {
                ClientId = clientId,
                ClientName = "CORS Edit Client",
                RequireClientSecret = false,
                RequirePkce = true,
                AllowedGrantTypes = new List<string> { "authorization_code" },
                RedirectUris = new List<string> { "https://example.com/callback" }
            }.ToEntity());
            await db.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.UpdateClientAuthenticationAsync(ClientId.Create(clientId), new ClientAuthenticationInputModel
            {
                RequirePkce = true,
                RequireClientSecret = false,
                GrantTypes = new List<string> { "authorization_code" },
                RedirectUris = new List<string> { "https://example.com/callback" },
                CorsOrigins = new List<string> { "http://LOCALHOST:5173/", "http://localhost:5173" }
            });
            Assert.True(result.Succeeded, result.ErrorMessage);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var client = await sp.GetRequiredService<ConfigurationDbContext>().Clients
                .AsNoTracking()
                .Include(entity => entity.AllowedCorsOrigins)
                .SingleAsync(entity => entity.ClientId == clientId);
            Assert.Equal("http://localhost:5173", Assert.Single(client.AllowedCorsOrigins).Origin);
        });
    }

    [Fact]
    public async Task UpdateClientAuthenticationAsync_OverlongCollectionValues_ReturnsFieldErrorsAndDoesNotMutate()
    {
        var clientId = $"length-edit-{Guid.NewGuid():N}";
        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            db.Clients.Add(new Client
            {
                ClientId = clientId,
                ClientName = "Length Validation Client",
                RequireClientSecret = false,
                RequirePkce = true,
                AllowedGrantTypes = new List<string> { "authorization_code" },
                RedirectUris = new List<string> { "https://example.com/callback" },
                PostLogoutRedirectUris = new List<string> { "https://example.com/signed-out" },
                AllowedCorsOrigins = new List<string> { "https://example.com" }
            }.ToEntity());
            await db.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var input = new ClientAuthenticationInputModel
            {
                GrantTypes = new List<string> { new('g', ValidationConstants.MaxGrantTypeLength + 1) },
                RedirectUris = new List<string> { "https://example.com/" + new string('r', ValidationConstants.MaxClientRedirectUriLength) },
                PostLogoutRedirectUris = new List<string> { "https://example.com/" + new string('p', ValidationConstants.MaxClientPostLogoutRedirectUriLength) },
                CorsOrigins = new List<string> { "https://example.com/" + new string('c', ValidationConstants.MaxClientCorsOriginLength) }
            };

            var result = await service.UpdateClientAuthenticationAsync(ClientId.Create(clientId), input);

            Assert.False(result.Succeeded);
            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
            Assert.Contains("Input.GrantTypes", result.Errors.Keys);
            Assert.Contains("Input.RedirectUris", result.Errors.Keys);
            Assert.Contains("Input.PostLogoutRedirectUris", result.Errors.Keys);
            Assert.Contains("Input.CorsOrigins", result.Errors.Keys);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            var entity = await db.Clients
                .AsNoTracking()
                .Include(client => client.AllowedGrantTypes)
                .Include(client => client.RedirectUris)
                .Include(client => client.PostLogoutRedirectUris)
                .Include(client => client.AllowedCorsOrigins)
                .SingleAsync(client => client.ClientId == clientId);
            Assert.Equal("authorization_code", entity.AllowedGrantTypes.Single().GrantType);
            Assert.Equal("https://example.com/callback", entity.RedirectUris.Single().RedirectUri);
            Assert.Equal("https://example.com/signed-out", entity.PostLogoutRedirectUris.Single().PostLogoutRedirectUri);
            Assert.Equal("https://example.com", entity.AllowedCorsOrigins.Single().Origin);
        });
    }

    [Fact]
    public async Task UpdateClientTokenSettingsAsync_InvalidTokenLifetime_FailsValidation()
    {
        var tag = Guid.NewGuid().ToString("N");
        var clientId = $"{tag}-client";

        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            db.Clients.Add(new Client
            {
                ClientId = clientId,
                ClientName = "Test Client",
                RequireClientSecret = false,
                RequirePkce = true,
                AllowedGrantTypes = new List<string> { "authorization_code" },
                RedirectUris = new List<string> { "https://example.com/callback" }
            }.ToEntity());
            await db.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var detailsService = sp.GetRequiredService<IClientDetailsService>();

            var input = new ClientTokenSettingsInputModel
            {
                AccessTokenLifetime = TokenLifetime.FromSeconds(-1),
                IdentityTokenLifetime = TokenLifetime.FromSeconds(300
            )};

            var result = await detailsService.UpdateClientTokenSettingsAsync(ClientId.Create(clientId), input);

            Assert.False(result.Succeeded);
        });
    }
}
