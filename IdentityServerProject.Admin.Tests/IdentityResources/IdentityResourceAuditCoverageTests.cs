using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Data;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.IdentityResources;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.IdentityResources;

/// <summary>
/// Verifies TASK-04 audit coverage for <see cref="IdentityResourceEditorService"/> (previously
/// success-only) and <see cref="IdentityResourceListService.DeleteIdentityResourceAsync"/>
/// (previously had no <see cref="IAuditWriter"/> dependency at all - a named plan gap).
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
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.IdentityResources.Add(resource);
            await configDb.SaveChangesAsync();
        });
    }

    private async Task<AuditLogEntry> GetSingleAuditEntryAsync(string action, string targetId)
    {
        AuditLogEntry? found = null;
        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            found = db.AuditLogEntries.Single(e => e.Category == AuditCategory.IdentityResource && e.Action == action && e.TargetId == targetId);
            await Task.CompletedTask;
        });
        return found!;
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_ProtectedName_WritesDeniedAuditEvent()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceListService>();
            var result = await service.DeleteIdentityResourceAsync(BuiltInIdentityResourcePolicy.OpenIdResourceName);
            Assert.Equal(IdentityResourceDeleteResult.Blocked, result);
        });

        var entry = await GetSingleAuditEntryAsync(AuditAction.Delete, BuiltInIdentityResourcePolicy.OpenIdResourceName);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.ProtectedResource, entry.ReasonCode);
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_NonEditableResource_WritesDeniedAuditEvent()
    {
        var name = $"idres-audit-noneditable-{Guid.NewGuid():N}";
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
            var service = sp.GetRequiredService<IIdentityResourceListService>();
            var result = await service.DeleteIdentityResourceAsync(name);
            Assert.Equal(IdentityResourceDeleteResult.Blocked, result);
        });

        var entry = await GetSingleAuditEntryAsync(AuditAction.Delete, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.ProtectedResource, entry.ReasonCode);
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_ResourceNotFound_WritesDeniedAuditEvent()
    {
        var name = $"idres-audit-delete-missing-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceListService>();
            var result = await service.DeleteIdentityResourceAsync(name);
            Assert.Equal(IdentityResourceDeleteResult.NotFound, result);
        });

        var entry = await GetSingleAuditEntryAsync(AuditAction.Delete, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_EditableResourceExists_WritesSucceededAuditEvent()
    {
        var name = $"idres-audit-delete-{Guid.NewGuid():N}";
        await SeedAsync(new IdentityResource
        {
            Name = name,
            DisplayName = "Delete Me",
            Enabled = true,
            UserClaims = new List<IdentityResourceClaim>()
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceListService>();
            var result = await service.DeleteIdentityResourceAsync(name);
            Assert.Equal(IdentityResourceDeleteResult.Deleted, result);
        });

        var entry = await GetSingleAuditEntryAsync(AuditAction.Delete, name);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task CreateAsync_ProtectedName_WritesDeniedAuditEvent()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceEditorService>();
            var result = await service.CreateAsync(
                BuiltInIdentityResourcePolicy.OpenIdResourceName, "OpenId", null,
                enabled: true, required: true, emphasize: false, showInDiscoveryDocument: true,
                userClaims: new List<string>());
            Assert.False(result.Succeeded);
        });

        var entry = await GetSingleAuditEntryAsync(AuditAction.Create, BuiltInIdentityResourcePolicy.OpenIdResourceName);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.ProtectedResource, entry.ReasonCode);
    }

    [Fact]
    public async Task CreateAsync_ValidInput_WritesSucceededAuditEvent()
    {
        var name = $"idres-audit-create-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceEditorService>();
            var result = await service.CreateAsync(
                name, "New Resource", null,
                enabled: true, required: false, emphasize: false, showInDiscoveryDocument: true,
                userClaims: new List<string>());
            Assert.True(result.Succeeded);
        });

        var entry = await GetSingleAuditEntryAsync(AuditAction.Create, name);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task UpdateBasicsAsync_ResourceNotFound_WritesDeniedAuditEvent()
    {
        var name = $"idres-audit-update-missing-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceEditorService>();
            var result = await service.UpdateBasicsAsync(
                name, "New Display", null,
                enabled: true, required: false, emphasize: false, showInDiscoveryDocument: true);
            Assert.Equal(IdentityResourceEditOutcome.NotFound, result.Outcome);
        });

        var entry = await GetSingleAuditEntryAsync(AuditAction.Update, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task UpdateBasicsAsync_UnexpectedPersistenceFailure_WritesFailedAuditEventAndRethrows()
    {
        var name = $"idres-audit-failure-{Guid.NewGuid():N}";

        await Assert.ThrowsAnyAsync<Exception>(() => _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceEditorService>();
            await sp.GetRequiredService<ConfigurationDbContext>().DisposeAsync();
            await service.UpdateBasicsAsync(
                name, "Display", null,
                enabled: true, required: false, emphasize: false, showInDiscoveryDocument: true);
        }));

        var entry = await GetSingleAuditEntryAsync(AuditAction.Update, name);
        Assert.Equal(AuditOutcome.Failed, entry.Outcome);
        Assert.Equal(AuditReasonCode.PersistenceFailure, entry.ReasonCode);
    }
}
