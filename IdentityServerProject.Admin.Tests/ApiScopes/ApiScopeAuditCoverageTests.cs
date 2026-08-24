using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Data;
using IdentityServerProject.Services.ApiScopes;
using IdentityServerProject.Services.AuditLogs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.ApiScopes;

/// <summary>
/// Verifies TASK-04 audit coverage for <see cref="ApiScopeEditorService"/> (previously
/// success-only) and <see cref="ApiScopeListService.DeleteApiScopeAsync"/> (previously had
/// no <see cref="IAuditWriter"/> dependency at all - a named plan gap).
/// </summary>
public class ApiScopeAuditCoverageTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ApiScopeAuditCoverageTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedApiScopeAsync(ApiScope scope)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiScopes.Add(scope);
            await configDb.SaveChangesAsync();
        });
    }

    private async Task<AuditLogEntry> GetSingleAuditEntryAsync(string action, string targetId)
    {
        AuditLogEntry? found = null;
        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            found = db.AuditLogEntries.Single(e => e.Category == AuditCategories.ApiScope && e.Action == action && e.TargetId == targetId);
            await Task.CompletedTask;
        });
        return found!;
    }

    [Fact]
    public async Task DeleteApiScopeAsync_ScopeNotFound_WritesDeniedAuditEvent()
    {
        var name = $"apiscope-audit-delete-missing-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();
            var result = await service.DeleteApiScopeAsync(name);
            Assert.Equal(ApiScopeDeleteResult.NotFound, result);
        });

        var entry = await GetSingleAuditEntryAsync(AuditActions.Delete, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCodes.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task DeleteApiScopeAsync_ScopeExists_WritesSucceededAuditEvent()
    {
        var name = $"apiscope-audit-delete-{Guid.NewGuid():N}";
        await SeedApiScopeAsync(new ApiScope { Name = name, DisplayName = "Delete Me", Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();
            var result = await service.DeleteApiScopeAsync(name);
            Assert.Equal(ApiScopeDeleteResult.Deleted, result);
        });

        var entry = await GetSingleAuditEntryAsync(AuditActions.Delete, name);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task CreateAsync_NameCollision_WritesDeniedAuditEvent()
    {
        var name = $"apiscope-audit-create-collision-{Guid.NewGuid():N}";
        await SeedApiScopeAsync(new ApiScope { Name = name, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeEditorService>();
            var result = await service.CreateAsync(name, "Duplicate", null);
            Assert.False(result.Success);
        });

        var entry = await GetSingleAuditEntryAsync(AuditActions.Create, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCodes.NameCollision, entry.ReasonCode);
    }

    [Fact]
    public async Task CreateAsync_ValidInput_WritesSucceededAuditEvent()
    {
        var name = $"apiscope-audit-create-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeEditorService>();
            var result = await service.CreateAsync(name, "New Scope", null);
            Assert.True(result.Success);
        });

        var entry = await GetSingleAuditEntryAsync(AuditActions.Create, name);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task UpdateBasicsAsync_ScopeNotFound_WritesDeniedAuditEvent()
    {
        var name = $"apiscope-audit-update-missing-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeEditorService>();
            var result = await service.UpdateBasicsAsync(name, "New Display", null, true, false, false, true);
            Assert.False(result);
        });

        var entry = await GetSingleAuditEntryAsync(AuditActions.Update, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCodes.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task UpdateBasicsAsync_UnexpectedPersistenceFailure_WritesFailedAuditEventAndRethrows()
    {
        var name = $"apiscope-audit-failure-{Guid.NewGuid():N}";

        await Assert.ThrowsAnyAsync<Exception>(() => _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeEditorService>();
            await sp.GetRequiredService<ConfigurationDbContext>().DisposeAsync();
            await service.UpdateBasicsAsync(name, "Display", null, true, false, false, true);
        }));

        var entry = await GetSingleAuditEntryAsync(AuditActions.Update, name);
        Assert.Equal(AuditOutcome.Failed, entry.Outcome);
        Assert.Equal(AuditReasonCodes.PersistenceFailure, entry.ReasonCode);
    }
}
