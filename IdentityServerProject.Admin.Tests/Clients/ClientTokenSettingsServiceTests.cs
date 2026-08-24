using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Duende.IdentityServer.Models;
using IdentityServerProject.Data;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientTokenSettingsServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ClientTokenSettingsServiceTests(AdminWebFactory factory)
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
    public async Task GetClientTokenSettingsAsync_NonExistentClient_ReturnsNull()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.GetClientTokenSettingsAsync("non-existent-client-id-xyz");
            Assert.Null(result);
        });
    }

    [Fact]
    public async Task GetClientTokenSettingsAsync_ExistingClient_ReturnsCurrentSettings()
    {
        var clientId = "token-settings-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "Token Settings Client",
            AccessTokenLifetime = 3600,
            IdentityTokenLifetime = 300,
            RequireConsent = false,
            AllowOfflineAccess = true
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.GetClientTokenSettingsAsync(clientId);

            Assert.NotNull(result);
            Assert.Equal(3600, result!.AccessTokenLifetime);
            Assert.Equal(300, result.IdentityTokenLifetime);
            Assert.False(result.RequireConsent);
            Assert.True(result.AllowOfflineAccess);
        });
    }

    [Fact]
    public async Task UpdateClientTokenSettingsAsync_ExistingClient_UpdatesAllFields()
    {
        var clientId = "token-settings-update-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "Token Settings Update Client",
            RequireClientSecret = false,
            RequirePkce = true,
            AllowedGrantTypes = new[] { "authorization_code" },
            RedirectUris = new[] { "https://example.test/callback" },
            AccessTokenLifetime = 3600,
            IdentityTokenLifetime = 300,
            RequireConsent = false,
            AllowOfflineAccess = false,
            RefreshTokenUsage = TokenUsage.ReUse,
            RefreshTokenExpiration = TokenExpiration.Absolute,
            AbsoluteRefreshTokenLifetime = 86400,
            SlidingRefreshTokenLifetime = 43200
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();

            var input = new ClientTokenSettingsInputModel
            {
                AccessTokenLifetime = 7200,
                IdentityTokenLifetime = 600,
                RequireConsent = true,
                AllowOfflineAccess = true,
                RefreshTokenUsage = (int)TokenUsage.OneTimeOnly,
                RefreshTokenExpiration = (int)TokenExpiration.Sliding,
                AbsoluteRefreshTokenLifetime = 172800,
                SlidingRefreshTokenLifetime = 72000
            };

            var updateResult = await service.UpdateClientTokenSettingsAsync(clientId, input);
            Assert.True(updateResult.Succeeded, updateResult.ErrorMessage);

            var result = await service.GetClientTokenSettingsAsync(clientId);
            Assert.NotNull(result);
            Assert.Equal(7200, result!.AccessTokenLifetime);
            Assert.Equal(600, result.IdentityTokenLifetime);
            Assert.True(result.RequireConsent);
            Assert.True(result.AllowOfflineAccess);
            Assert.Equal((int)TokenUsage.OneTimeOnly, result.RefreshTokenUsage);
            Assert.Equal((int)TokenExpiration.Sliding, result.RefreshTokenExpiration);
            Assert.Equal(172800, result.AbsoluteRefreshTokenLifetime);
            Assert.Equal(72000, result.SlidingRefreshTokenLifetime);

            var audit = await sp.GetRequiredService<ApplicationDbContext>().AuditLogEntries
                .SingleAsync(entry => entry.Category == AuditCategories.Client
                    && entry.Action == AuditActions.UpdateTokenSettings
                    && entry.TargetId == clientId);
            Assert.Contains("AccessTokenLifetime", audit.OldValuesJson);
            Assert.Contains("SlidingRefreshTokenLifetime", audit.NewValuesJson);
            Assert.DoesNotContain("Secret", audit.NewValuesJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password", audit.NewValuesJson, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task UpdateClientTokenSettingsAsync_DisablingOfflineAccess_RetainsRefreshConfiguration()
    {
        var clientId = "token-settings-disable-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "Disable Offline Access Client",
            RequireClientSecret = false,
            RequirePkce = true,
            AllowedGrantTypes = new[] { "authorization_code" },
            RedirectUris = new[] { "https://example.test/callback" },
            AllowOfflineAccess = true,
            RefreshTokenUsage = TokenUsage.OneTimeOnly,
            RefreshTokenExpiration = TokenExpiration.Sliding,
            AbsoluteRefreshTokenLifetime = 172800,
            SlidingRefreshTokenLifetime = 72000
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.UpdateClientTokenSettingsAsync(clientId, new ClientTokenSettingsInputModel
            {
                AccessTokenLifetime = 3600,
                IdentityTokenLifetime = 300,
                AllowOfflineAccess = false,
                RefreshTokenUsage = (int)TokenUsage.ReUse,
                RefreshTokenExpiration = (int)TokenExpiration.Absolute,
                AbsoluteRefreshTokenLifetime = 60,
                SlidingRefreshTokenLifetime = 60
            });

            Assert.True(result.Succeeded, result.ErrorMessage);
            var saved = await service.GetClientTokenSettingsAsync(clientId);
            Assert.NotNull(saved);
            Assert.False(saved!.AllowOfflineAccess);
            Assert.Equal((int)TokenUsage.OneTimeOnly, saved.RefreshTokenUsage);
            Assert.Equal((int)TokenExpiration.Sliding, saved.RefreshTokenExpiration);
            Assert.Equal(172800, saved.AbsoluteRefreshTokenLifetime);
            Assert.Equal(72000, saved.SlidingRefreshTokenLifetime);
        });
    }

    [Fact]
    public async Task UpdateClientTokenSettingsAsync_NonExistentClient_ReturnsFalse()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.UpdateClientTokenSettingsAsync("non-existent-client-id-abc", new ClientTokenSettingsInputModel
            {
                AccessTokenLifetime = 3600,
                IdentityTokenLifetime = 300
            });
            Assert.Equal(AdminMutationStatus.NotFound, result.Status);
        });
    }

    [Theory]
    [InlineData(86401, 300, "Input.AccessTokenLifetime")]
    [InlineData(3600, 3601, "Input.IdentityTokenLifetime")]
    public async Task UpdateClientTokenSettingsAsync_AboveUiMaximum_IsFieldKeyedAndDoesNotMutate(
        int accessLifetime,
        int identityLifetime,
        string expectedField)
    {
        var clientId = "token-settings-invalid-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "Token Settings Validation Client",
            RequireClientSecret = false,
            RequirePkce = true,
            AllowedGrantTypes = new[] { "authorization_code" },
            RedirectUris = new[] { "https://example.test/callback" },
            AccessTokenLifetime = 3600,
            IdentityTokenLifetime = 300
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var result = await service.UpdateClientTokenSettingsAsync(clientId, new ClientTokenSettingsInputModel
            {
                AccessTokenLifetime = accessLifetime,
                IdentityTokenLifetime = identityLifetime
            });

            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
            Assert.True(result.Errors.ContainsKey(expectedField));

            var persisted = await sp.GetRequiredService<ConfigurationDbContext>().Clients
                .AsNoTracking().SingleAsync(client => client.ClientId == clientId);
            Assert.Equal(3600, persisted.AccessTokenLifetime);
            Assert.Equal(300, persisted.IdentityTokenLifetime);
        });
    }
}
