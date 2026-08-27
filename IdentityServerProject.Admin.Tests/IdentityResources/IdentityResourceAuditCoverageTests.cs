using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.IdentityResources;
using IdentityServerProject.Services.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.IdentityResources;

/// <summary>
///     Verifies TASK-04 audit coverage for <see cref="IdentityResourceEditorService" /> (previously
///     success-only) and <see cref="IdentityResourceListService.DeleteIdentityResourceAsync" />
///     (previously had no <see cref="IAuditWriter" /> dependency at all - a named plan gap).
/// </summary>
public class IdentityResourceAuditCoverageTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public IdentityResourceAuditCoverageTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedAsync(IdentityResource resource)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.IdentityResources.Add(resource);
            await configDb.SaveChangesAsync();
        });
    }

    private async Task<AuditLogEntry> GetSingleAuditEntryAsync(string action, string targetId)
    {
        AuditLogEntry? found = null;
        await _factory.RunInScopeAsync(async sp =>
        {
            ApplicationDbContext db = sp.GetRequiredService<ApplicationDbContext>();
            found = db.AuditLogEntries.Single(e =>
                e.Category == AuditCategory.IdentityResource && e.Action == action && e.TargetId == targetId);
            await Task.CompletedTask;
        });
        return found!;
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_ProtectedName_WritesDeniedAuditEvent()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IIdentityResourceListService service = sp.GetRequiredService<IIdentityResourceListService>();
            IdentityResourceDeleteResult result =
                await service.DeleteIdentityResourceAsync(BuiltInIdentityResourcePolicy.OpenIdResourceName);
            Assert.Equal(IdentityResourceDeleteResult.Blocked, result);
        });

        AuditLogEntry entry =
            await GetSingleAuditEntryAsync(AuditAction.Delete, BuiltInIdentityResourcePolicy.OpenIdResourceName);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.ProtectedResource, entry.ReasonCode);
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_NonEditableResource_WritesDeniedAuditEvent()
    {
        string name = $"idres-audit-noneditable-{Guid.NewGuid():N}";
        await SeedAsync(new IdentityResource
        {
            Name = name,
            DisplayName = "Non-editable",
            Enabled = true,
            NonEditable = true,
            UserClaims = new List<IdentityResourceClaim>()
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IIdentityResourceListService service = sp.GetRequiredService<IIdentityResourceListService>();
            IdentityResourceDeleteResult result = await service.DeleteIdentityResourceAsync(name);
            Assert.Equal(IdentityResourceDeleteResult.Blocked, result);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Delete, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.ProtectedResource, entry.ReasonCode);
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_ResourceNotFound_WritesDeniedAuditEvent()
    {
        string name = $"idres-audit-delete-missing-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            IIdentityResourceListService service = sp.GetRequiredService<IIdentityResourceListService>();
            IdentityResourceDeleteResult result = await service.DeleteIdentityResourceAsync(name);
            Assert.Equal(IdentityResourceDeleteResult.NotFound, result);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Delete, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_EditableResourceExists_WritesSucceededAuditEvent()
    {
        string name = $"idres-audit-delete-{Guid.NewGuid():N}";
        await SeedAsync(new IdentityResource
        {
            Name = name,
            DisplayName = "Delete Me",
            Enabled = true,
            UserClaims = new List<IdentityResourceClaim>()
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IIdentityResourceListService service = sp.GetRequiredService<IIdentityResourceListService>();
            IdentityResourceDeleteResult result = await service.DeleteIdentityResourceAsync(name);
            Assert.Equal(IdentityResourceDeleteResult.Deleted, result);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Delete, name);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task CreateAsync_ProtectedName_WritesDeniedAuditEvent()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IIdentityResourceEditorService service = sp.GetRequiredService<IIdentityResourceEditorService>();
            AdminMutationResult result = await service.CreateAsync(
                BuiltInIdentityResourcePolicy.OpenIdResourceName, "OpenId", null,
                true, true, false, true,
                new List<string>());
            Assert.False(result.Succeeded);
        });

        AuditLogEntry entry =
            await GetSingleAuditEntryAsync(AuditAction.Create, BuiltInIdentityResourcePolicy.OpenIdResourceName);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.ProtectedResource, entry.ReasonCode);
    }

    [Fact]
    public async Task CreateAsync_ValidInput_WritesSucceededAuditEvent()
    {
        string name = $"idres-audit-create-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            IIdentityResourceEditorService service = sp.GetRequiredService<IIdentityResourceEditorService>();
            AdminMutationResult result = await service.CreateAsync(
                name, "New Resource", null,
                true, false, false, true,
                new List<string>());
            Assert.True(result.Succeeded);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Create, name);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task UpdateBasicsAsync_ResourceNotFound_WritesDeniedAuditEvent()
    {
        string name = $"idres-audit-update-missing-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            IIdentityResourceEditorService service = sp.GetRequiredService<IIdentityResourceEditorService>();
            IdentityResourceEditResult result = await service.UpdateBasicsAsync(
                name, "New Display", null,
                true, false, false, true);
            Assert.Equal(IdentityResourceEditOutcome.NotFound, result.Outcome);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Update, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task UpdateBasicsAsync_UnexpectedPersistenceFailure_WritesFailedAuditEventAndRethrows()
    {
        string name = $"idres-audit-failure-{Guid.NewGuid():N}";

        await Assert.ThrowsAnyAsync<Exception>(() => _factory.RunInScopeAsync(async sp =>
        {
            IIdentityResourceEditorService service = sp.GetRequiredService<IIdentityResourceEditorService>();
            await sp.GetRequiredService<ConfigurationDbContext>().DisposeAsync();
            await service.UpdateBasicsAsync(
                name, "Display", null,
                true, false, false, true);
        }));

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Update, name);
        Assert.Equal(AuditOutcome.Failed, entry.Outcome);
        Assert.Equal(AuditReasonCode.PersistenceFailure, entry.ReasonCode);
    }
}