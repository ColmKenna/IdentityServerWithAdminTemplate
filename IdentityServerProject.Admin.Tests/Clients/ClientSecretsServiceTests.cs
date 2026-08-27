using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Duende.IdentityServer.Models;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientSecretsServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ClientSecretsServiceTests(AdminWebFactory factory)
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
    public async Task GetClientSecretsAsync_NonExistentClient_ReturnsNull()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ClientSecretsModel? result =
                await service.GetClientSecretsAsync(ClientId.Create("non-existent-client-id-xyz"));
            Assert.Null(result);
        });
    }

    [Fact]
    public async Task GetClientSecretsAsync_ExistingClient_ReturnsMetadataOnly()
    {
        string clientId = "secrets-client-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "Secrets Test Client",
            RequireClientSecret = true,
            ClientSecrets = new List<Secret>
            {
                new("hashed-value-1") { Description = "First secret" }
            }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ClientSecretsModel? result = await service.GetClientSecretsAsync(ClientId.Create(clientId));

            Assert.NotNull(result);
            Assert.True(result!.RequireClientSecret);
            Assert.Single(result.Secrets);
            Assert.Equal("First secret", result.Secrets[0].Description);
        });
    }

    [Fact]
    public async Task GenerateClientSecretAsync_ExistingClient_AddsHashedSecretAndReturnsPlaintextOnce()
    {
        string clientId = "generate-secret-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "Generate Secret Client",
            RequireClientSecret = true
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();

            ClientSecretGenerateResult result =
                await service.GenerateClientSecretAsync(ClientId.Create(clientId), "New secret");

            Assert.True(result.Success);
            Assert.False(string.IsNullOrWhiteSpace(result.PlaintextSecret));

            Duende.IdentityServer.EntityFramework.Entities.Client entity = await configDb.Clients
                .Include(c => c.ClientSecrets)
                .SingleAsync(c => c.ClientId == clientId);

            Assert.Single(entity.ClientSecrets);
            Assert.NotEqual(result.PlaintextSecret, entity.ClientSecrets[0].Value);
        });
    }

    [Fact]
    public async Task GenerateClientSecretAsync_NonExistentClient_ReturnsFailure()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ClientSecretGenerateResult result =
                await service.GenerateClientSecretAsync(ClientId.Create("non-existent-client-id-abc"), null);
            Assert.False(result.Success);
        });
    }

    [Fact]
    public async Task GenerateClientSecretAsync_OverlongDescription_ReturnsFieldErrorAndDoesNotMutate()
    {
        string clientId = "generate-secret-validation-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "Generate Secret Validation Client",
            RequireClientSecret = true
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();

            ClientSecretGenerateResult result = await service.GenerateClientSecretAsync(
                ClientId.Create(clientId),
                new string('x', ValidationConstants.MaxClientSecretDescriptionLength + 1));

            Assert.False(result.Success);
            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
            Assert.True(result.Errors.ContainsKey("Description"));

            Duende.IdentityServer.EntityFramework.Entities.Client entity = await configDb.Clients
                .AsNoTracking()
                .Include(c => c.ClientSecrets)
                .SingleAsync(c => c.ClientId == clientId);
            Assert.Empty(entity.ClientSecrets);
        });
    }

    [Fact]
    public async Task RevokeClientSecretAsync_NotLastSecret_Succeeds()
    {
        string clientId = "revoke-not-last-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "Revoke Not Last Client",
            RequireClientSecret = true,
            ClientSecrets = new List<Secret>
            {
                new("hashed-1") { Description = "Secret A" },
                new("hashed-2") { Description = "Secret B" }
            }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ClientSecretsModel? secrets = await service.GetClientSecretsAsync(ClientId.Create(clientId));
            int targetId = secrets!.Secrets.First().Id;

            ClientSecretRevokeResult
                result = await service.RevokeClientSecretAsync(ClientId.Create(clientId), targetId);
            Assert.True(result.Success);

            ClientSecretsModel? afterRevoke = await service.GetClientSecretsAsync(ClientId.Create(clientId));
            Assert.Single(afterRevoke!.Secrets);
            Assert.DoesNotContain(afterRevoke.Secrets, s => s.Id == targetId);
        });
    }

    [Fact]
    public async Task RevokeClientSecretAsync_LastSecretOnConfidentialClient_IsBlocked()
    {
        string clientId = "revoke-last-confidential-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "Revoke Last Confidential Client",
            RequireClientSecret = true,
            ClientSecrets = new List<Secret>
            {
                new("hashed-only") { Description = "Only secret" }
            }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ClientSecretsModel? secrets = await service.GetClientSecretsAsync(ClientId.Create(clientId));
            int targetId = secrets!.Secrets.Single().Id;

            ClientSecretRevokeResult
                result = await service.RevokeClientSecretAsync(ClientId.Create(clientId), targetId);

            Assert.False(result.Success);
            Assert.Contains("last usable secret", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(AuditReasonCode.LastUsableSecret, result.ReasonCode);

            ClientSecretsModel? afterAttempt = await service.GetClientSecretsAsync(ClientId.Create(clientId));
            Assert.Single(afterAttempt!.Secrets);
        });
    }

    [Fact]
    public async Task RevokeClientSecretAsync_LastSecretOnPublicClient_Succeeds()
    {
        // Client does not require a secret to authenticate, so removing its last stray secret is safe.
        string clientId = "revoke-last-public-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "Revoke Last Public Client",
            RequireClientSecret = false,
            ClientSecrets = new List<Secret>
            {
                new("hashed-stray") { Description = "Stray secret" }
            }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ClientSecretsModel? secrets = await service.GetClientSecretsAsync(ClientId.Create(clientId));
            int targetId = secrets!.Secrets.Single().Id;

            ClientSecretRevokeResult
                result = await service.RevokeClientSecretAsync(ClientId.Create(clientId), targetId);
            Assert.True(result.Success);

            ClientSecretsModel? afterRevoke = await service.GetClientSecretsAsync(ClientId.Create(clientId));
            Assert.Empty(afterRevoke!.Secrets);
        });
    }

    [Fact]
    public async Task RevokeClientSecretAsync_NonExistentSecret_ReturnsFailure()
    {
        string clientId = "revoke-nonexistent-secret-" + Guid.NewGuid().ToString("N");
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "Revoke Nonexistent Secret Client",
            RequireClientSecret = true,
            ClientSecrets = new List<Secret> { new("hashed") { Description = "A" } }
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ClientSecretRevokeResult result = await service.RevokeClientSecretAsync(ClientId.Create(clientId), -1);
            Assert.False(result.Success);
        });
    }

    [Fact]
    public async Task RevokeClientSecretAsync_NonExistentClient_ReturnsFailure()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IClientDetailsService service = sp.GetRequiredService<IClientDetailsService>();
            ClientSecretRevokeResult result =
                await service.RevokeClientSecretAsync(ClientId.Create("non-existent-client-id-abc"), 1);
            Assert.False(result.Success);
        });
    }
}