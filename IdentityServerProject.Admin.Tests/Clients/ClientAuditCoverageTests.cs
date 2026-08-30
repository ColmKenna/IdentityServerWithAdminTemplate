using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Duende.IdentityServer.Models;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Clients;

/// <summary>
///     Verifies TASK-04 audit coverage for <see cref="ClientCreateService" /> and
///     <see cref="ClientDetailsService" />: both already audited success, but neither
///     audited denial or unexpected-failure outcomes before this task.
/// </summary>
public class ClientAuditCoverageTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ClientAuditCoverageTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static Client MakeClient(string clientId, string clientName, bool enabled = true)
    {
        return new Client
        {
            ClientId = clientId,
            ClientName = clientName,
            Description = "Test description",
            Enabled = enabled,
            RequirePkce = true,
            RequireClientSecret = true,
            AllowedGrantTypes = new List<string> { "authorization_code" },
            RedirectUris = new List<string> { "https://localhost:5001/signin-oidc" },
            AllowedCorsOrigins = new List<string>(),
            ClientSecrets = new List<Secret> { new("hashed_secret") },
            AllowedScopes = new List<string> { "openid", "profile" }
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

    private async Task SeedIdentityScopesAsync()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            if (!await configDb.IdentityResources.AnyAsync(resource => resource.Name == "openid"))
            {
                configDb.IdentityResources.Add(new IdentityResource("openid", new[] { "sub" }).ToEntity());
                configDb.IdentityResources.Add(new IdentityResource("profile", new[] { "name" }).ToEntity());
                await configDb.SaveChangesAsync();
            }
        });
    }

    private async Task<AuditLogEntry> GetSingleAuditEntryAsync(string action, string targetId)
    {
        AuditLogEntry? found = null;
        await _factory.RunInScopeAsync(async sp =>
        {
            ApplicationDbContext db = sp.GetRequiredService<ApplicationDbContext>();
            found = db.AuditLogEntries.Single(e =>
                e.Category == AuditCategory.Client && e.Action == action && e.TargetId == targetId);
            await Task.CompletedTask;
        });
        return found!;
    }

    [Fact]
    public async Task CreateClientAsync_DuplicateClientId_WritesDeniedAuditEvent()
    {
        string clientId = $"client-audit-dup-{Guid.NewGuid():N}";
        await SeedAsync(MakeClient(clientId, "Existing Client"));

        var input = new ClientCreateInputModel
        {
            ClientId = clientId,
            ClientName = "Duplicate Attempt",
            RequirePkce = true,
            RequireClientSecret = false
        };

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientCreateService service = sp.GetRequiredService<IClientCreateService>();
            ClientCreateResult result = await service.CreateClientAsync(input);
            Assert.False(result.Success);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Create, clientId);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NameCollision, entry.ReasonCode);
    }

    [Fact]
    public async Task CreateClientAsync_ValidInput_WritesSucceededAuditEvent()
    {
        await SeedIdentityScopesAsync();
        string clientId = $"client-audit-create-{Guid.NewGuid():N}";
        var input = new ClientCreateInputModel
        {
            ClientId = clientId,
            ClientName = "New Client",
            RequirePkce = true,
            RequireClientSecret = false,
            GrantTypes = new List<string> { "authorization_code" },
            RedirectUris = new List<string> { "https://localhost:5001/signin-oidc" },
            AllowedScopes = new List<string> { "openid", "profile" }
        };

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientCreateService service = sp.GetRequiredService<IClientCreateService>();
            ClientCreateResult result = await service.CreateClientAsync(input);
            Assert.True(result.Success);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Create, clientId);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task DeleteClientAsync_EnabledClient_WritesDeniedAuditEventWithClientEnabledReason()
    {
        string clientId = $"client-audit-delete-enabled-{Guid.NewGuid():N}";
        await SeedAsync(MakeClient(clientId, "Enabled Client"));

        await _factory.RunInScopeAsync(async sp =>
        {
            ClientDetailsService service = sp.GetRequiredService<ClientDetailsService>();
            ClientDeleteResult result = await service.DeleteClientAsync(ClientId.Create(clientId));
            Assert.False(result.Success);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Delete, clientId);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.ClientEnabled, entry.ReasonCode);
    }

    [Fact]
    public async Task DeleteClientAsync_ClientNotFound_WritesDeniedAuditEvent()
    {
        string clientId = $"client-audit-delete-missing-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            ClientDetailsService service = sp.GetRequiredService<ClientDetailsService>();
            ClientDeleteResult result = await service.DeleteClientAsync(ClientId.Create(clientId));
            Assert.False(result.Success);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Delete, clientId);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task ToggleClientStatusAsync_ClientNotFound_WritesDeniedAuditEvent()
    {
        string clientId = $"client-audit-toggle-missing-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            ClientDetailsService service = sp.GetRequiredService<ClientDetailsService>();
            bool result = await service.ToggleClientStatusAsync(ClientId.Create(clientId));
            Assert.False(result);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.SetEnabled, clientId);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task ToggleClientStatusAsync_ExistingClient_WritesSucceededAuditEvent()
    {
        string clientId = $"client-audit-toggle-{Guid.NewGuid():N}";
        await SeedAsync(MakeClient(clientId, "Toggle Client"));

        await _factory.RunInScopeAsync(async sp =>
        {
            ClientDetailsService service = sp.GetRequiredService<ClientDetailsService>();
            bool result = await service.ToggleClientStatusAsync(ClientId.Create(clientId));
            Assert.True(result);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.SetEnabled, clientId);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task
        RevokeClientSecretAsync_LastUsableSecretOnConfidentialClient_WritesDeniedAuditEventWithLastUsableSecretReason()
    {
        string clientId = $"client-audit-lastsecret-{Guid.NewGuid():N}";
        await SeedAsync(MakeClient(clientId, "Confidential Client"));

        int secretId = 0;
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            Duende.IdentityServer.EntityFramework.Entities.Client entity =
                configDb.Clients.Include(c => c.ClientSecrets).Single(c => c.ClientId == clientId);
            secretId = entity.ClientSecrets.Single().Id;
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            ClientDetailsService service = sp.GetRequiredService<ClientDetailsService>();
            ClientSecretRevokeResult
                result = await service.RevokeClientSecretAsync(ClientId.Create(clientId), secretId);
            Assert.False(result.Success);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.RevokeSecret, clientId);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.LastUsableSecret, entry.ReasonCode);
    }

    [Fact]
    public async Task ToggleClientStatusAsync_UnexpectedPersistenceFailure_WritesFailedAuditEventAndRethrows()
    {
        string clientId = $"client-audit-failure-{Guid.NewGuid():N}";

        await Assert.ThrowsAnyAsync<Exception>(() => _factory.RunInScopeAsync(async sp =>
        {
            ClientDetailsService service = sp.GetRequiredService<ClientDetailsService>();
            await sp.GetRequiredService<ConfigurationDbContext>().DisposeAsync();
            await service.ToggleClientStatusAsync(ClientId.Create(clientId));
        }));

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.SetEnabled, clientId);
        Assert.Equal(AuditOutcome.Failed, entry.Outcome);
        Assert.Equal(AuditReasonCode.PersistenceFailure, entry.ReasonCode);
    }
}