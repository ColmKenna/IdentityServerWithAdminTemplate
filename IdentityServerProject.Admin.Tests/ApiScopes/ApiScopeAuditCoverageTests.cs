using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Services.ApiScopes;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.ApiScopes;

/// <summary>
///     Verifies TASK-04 audit coverage for <see cref="ApiScopeEditorService" /> (previously
///     success-only) and <see cref="ApiScopeListService.DeleteApiScopeAsync" /> (previously had
///     no <see cref="IAuditWriter" /> dependency at all - a named plan gap).
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
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiScopes.Add(scope);
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
                e.Category == AuditCategory.ApiScope && e.Action == action && e.TargetId == targetId);
            await Task.CompletedTask;
        });
        return found!;
    }

    [Fact]
    public async Task DeleteApiScopeAsync_ScopeNotFound_WritesDeniedAuditEvent()
    {
        string name = $"apiscope-audit-delete-missing-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiScopeListService service = sp.GetRequiredService<IApiScopeListService>();
            ApiScopeDeleteResult result = await service.DeleteApiScopeAsync(name);
            Assert.Equal(ApiScopeDeleteResult.NotFound, result);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Delete, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task DeleteApiScopeAsync_ScopeExists_WritesSucceededAuditEvent()
    {
        string name = $"apiscope-audit-delete-{Guid.NewGuid():N}";
        await SeedApiScopeAsync(new ApiScope { Name = name, DisplayName = "Delete Me", Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiScopeListService service = sp.GetRequiredService<IApiScopeListService>();
            ApiScopeDeleteResult result = await service.DeleteApiScopeAsync(name);
            Assert.Equal(ApiScopeDeleteResult.Deleted, result);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Delete, name);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task CreateAsync_NameCollision_WritesDeniedAuditEvent()
    {
        string name = $"apiscope-audit-create-collision-{Guid.NewGuid():N}";
        await SeedApiScopeAsync(new ApiScope { Name = name, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiScopeEditorService service = sp.GetRequiredService<IApiScopeEditorService>();
            AdminMutationResult result = await service.CreateAsync(name, "Duplicate", null);
            Assert.False(result.Succeeded);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Create, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NameCollision, entry.ReasonCode);
    }

    [Fact]
    public async Task CreateAsync_ValidInput_WritesSucceededAuditEvent()
    {
        string name = $"apiscope-audit-create-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiScopeEditorService service = sp.GetRequiredService<IApiScopeEditorService>();
            AdminMutationResult result = await service.CreateAsync(name, "New Scope", null);
            Assert.True(result.Succeeded);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Create, name);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task UpdateBasicsAsync_ScopeNotFound_WritesDeniedAuditEvent()
    {
        string name = $"apiscope-audit-update-missing-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiScopeEditorService service = sp.GetRequiredService<IApiScopeEditorService>();
            bool result = await service.UpdateBasicsAsync(name, "New Display", null, true, false, false, true);
            Assert.False(result);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Update, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task UpdateBasicsAsync_UnexpectedPersistenceFailure_WritesFailedAuditEventAndRethrows()
    {
        string name = $"apiscope-audit-failure-{Guid.NewGuid():N}";

        await Assert.ThrowsAnyAsync<Exception>(() => _factory.RunInScopeAsync(async sp =>
        {
            IApiScopeEditorService service = sp.GetRequiredService<IApiScopeEditorService>();
            await sp.GetRequiredService<ConfigurationDbContext>().DisposeAsync();
            await service.UpdateBasicsAsync(name, "Display", null, true, false, false, true);
        }));

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Update, name);
        Assert.Equal(AuditOutcome.Failed, entry.Outcome);
        Assert.Equal(AuditReasonCode.PersistenceFailure, entry.ReasonCode);
    }
}