using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Duende.IdentityServer.Models;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.Clients.Add(client.ToEntity());
            await configDb.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GetClientTokenSettingsAsync_NonExistentClient_ReturnsNull()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ClientTokenSettingsModel? result =
                await service.GetClientTokenSettingsAsync(ClientId.Create("non-existent-client-id-xyz"));
            Assert.Null(result);
        });
    }

    [Fact]
    public async Task GetClientTokenSettingsAsync_ExistingClient_ReturnsCurrentSettings()
    {
        string clientId = "token-settings-" + Guid.NewGuid().ToString("N");
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
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ClientTokenSettingsModel? result = await service.GetClientTokenSettingsAsync(ClientId.Create(clientId));

            Assert.NotNull(result);
            Assert.Equal(3600, result!.AccessTokenLifetime.Seconds);
            Assert.Equal(300, result.IdentityTokenLifetime.Seconds);
            Assert.False(result.RequireConsent);
            Assert.True(result.AllowOfflineAccess);
        });
    }

    [Fact]
    public async Task UpdateClientTokenSettingsAsync_ExistingClient_UpdatesAllFields()
    {
        string clientId = "token-settings-update-" + Guid.NewGuid().ToString("N");
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
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();

            var input = new ClientTokenSettingsInputModel
            {
                AccessTokenLifetime = TokenLifetime.FromSeconds(7200),
                IdentityTokenLifetime = TokenLifetime.FromSeconds(600),
                RequireConsent = true,
                AllowOfflineAccess = true,
                RefreshToken = new RefreshTokenSettings
                {
                    Usage = TokenUsage.OneTimeOnly,
                    Expiration = TokenExpiration.Sliding,
                    AbsoluteLifetime = TokenLifetime.FromSeconds(172800),
                    SlidingLifetime = TokenLifetime.FromSeconds(72000)
                }
            };

            AdminMutationResult updateResult =
                await service.UpdateClientTokenSettingsAsync(ClientId.Create(clientId), input);
            Assert.True(updateResult.Succeeded, updateResult.ErrorMessage);

            ClientTokenSettingsModel? result = await service.GetClientTokenSettingsAsync(ClientId.Create(clientId));
            Assert.NotNull(result);
            Assert.Equal(7200, result!.AccessTokenLifetime.Seconds);
            Assert.Equal(600, result.IdentityTokenLifetime.Seconds);
            Assert.True(result.RequireConsent);
            Assert.True(result.AllowOfflineAccess);
            Assert.Equal(TokenUsage.OneTimeOnly, result.RefreshToken.Usage);
            Assert.Equal(TokenExpiration.Sliding, result.RefreshToken.Expiration);
            Assert.Equal(172800, result.RefreshToken.AbsoluteLifetime.Seconds);
            Assert.Equal(72000, result.RefreshToken.SlidingLifetime.Seconds);

            AuditLogEntry audit = await sp.GetRequiredService<ApplicationDbContext>().AuditLogEntries
                .SingleAsync(entry => entry.Category == AuditCategory.Client
                                      && entry.Action == AuditAction.UpdateTokenSettings
                                      && entry.TargetId == clientId);
            Assert.Contains("AccessTokenLifetime", audit.OldValuesJson);
            Assert.Contains("SlidingLifetime", audit.NewValuesJson);
            Assert.DoesNotContain("Secret", audit.NewValuesJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password", audit.NewValuesJson, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task UpdateClientTokenSettingsAsync_DisablingOfflineAccess_RetainsRefreshConfiguration()
    {
        string clientId = "token-settings-disable-" + Guid.NewGuid().ToString("N");
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
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            AdminMutationResult result = await service.UpdateClientTokenSettingsAsync(ClientId.Create(clientId),
                new ClientTokenSettingsInputModel
                {
                    AccessTokenLifetime = TokenLifetime.FromSeconds(3600),
                    IdentityTokenLifetime = TokenLifetime.FromSeconds(300),
                    AllowOfflineAccess = false,
                    RefreshToken = new RefreshTokenSettings
                    {
                        Usage = TokenUsage.ReUse,
                        Expiration = TokenExpiration.Absolute,
                        AbsoluteLifetime = TokenLifetime.FromSeconds(60),
                        SlidingLifetime = TokenLifetime.FromSeconds(60)
                    }
                });

            Assert.True(result.Succeeded, result.ErrorMessage);
            ClientTokenSettingsModel? saved = await service.GetClientTokenSettingsAsync(ClientId.Create(clientId));
            Assert.NotNull(saved);
            Assert.False(saved!.AllowOfflineAccess);
            Assert.Equal(TokenUsage.OneTimeOnly, saved.RefreshToken.Usage);
            Assert.Equal(TokenExpiration.Sliding, saved.RefreshToken.Expiration);
            Assert.Equal(172800, saved.RefreshToken.AbsoluteLifetime.Seconds);
            Assert.Equal(72000, saved.RefreshToken.SlidingLifetime.Seconds);
        });
    }

    [Fact]
    public async Task UpdateClientTokenSettingsAsync_NonExistentClient_ReturnsFalse()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            AdminMutationResult result = await service.UpdateClientTokenSettingsAsync(
                ClientId.Create("non-existent-client-id-abc"), new ClientTokenSettingsInputModel
                {
                    AccessTokenLifetime = TokenLifetime.FromSeconds(3600),
                    IdentityTokenLifetime = TokenLifetime.FromSeconds(300)
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
        string clientId = "token-settings-invalid-" + Guid.NewGuid().ToString("N");
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
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            AdminMutationResult result = await service.UpdateClientTokenSettingsAsync(ClientId.Create(clientId),
                new ClientTokenSettingsInputModel
                {
                    AccessTokenLifetime = TokenLifetime.FromSeconds(accessLifetime),
                    IdentityTokenLifetime = TokenLifetime.FromSeconds(identityLifetime)
                });

            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
            Assert.True(result.Errors.ContainsKey(expectedField));

            Duende.IdentityServer.EntityFramework.Entities.Client persisted = await sp
                .GetRequiredService<ConfigurationDbContext>().Clients
                .AsNoTracking().SingleAsync(client => client.ClientId == clientId);
            Assert.Equal(3600, persisted.AccessTokenLifetime);
            Assert.Equal(300, persisted.IdentityTokenLifetime);
        });
    }
}