using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Grants;
using IdentityServerProject.Services.Users;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Grants;

/// <summary>
///     Verifies TASK-04 audit coverage for <see cref="GrantListService" />, which previously
///     had no <see cref="IAuditWriter" /> dependency at all.
/// </summary>
public class GrantAuditCoverageTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public GrantAuditCoverageTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static PersistedGrant MakeGrant(string key, string clientId, string subjectId)
    {
        return new PersistedGrant
        {
            Key = key,
            Type = "user_consent",
            ClientId = clientId,
            SubjectId = subjectId,
            CreationTime = DateTime.UtcNow.AddHours(-1),
            Expiration = DateTime.UtcNow.AddDays(7),
            Data = "{}"
        };
    }

    private async Task<AuditLogEntry> GetSingleAuditEntryAsync(string action, string targetId)
    {
        AuditLogEntry? result = null;
        await _factory.RunInScopeAsync(async sp =>
        {
            ApplicationDbContext db = sp.GetRequiredService<ApplicationDbContext>();
            result = db.AuditLogEntries.Single(e =>
                e.Category == AuditCategory.Grant && e.Action == action && e.TargetId == targetId);
            await Task.CompletedTask;
        });
        return result!;
    }

    [Fact]
    public async Task RevokeGrantAsync_ExistingGrant_WritesSucceededAuditEvent()
    {
        string tag = $"grant-audit-revoke-{Guid.NewGuid():N}";
        string grantKey = $"{tag}-key";
        await _factory.RunInScopeAsync(async sp =>
        {
            sp.GetRequiredService<PersistedGrantDbContext>().PersistedGrants
                .Add(MakeGrant(grantKey, $"{tag}-client", $"{tag}-user"));
            await sp.GetRequiredService<PersistedGrantDbContext>().SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IGrantListService service = sp.GetRequiredService<IGrantListService>();
            RevokeGrantResult result = await service.RevokeGrantAsync(GrantKey.Create(grantKey));
            Assert.Equal(RevokeGrantResult.Revoked, result);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Revoke, grantKey);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
        Assert.Equal(AuditReasonCode.Succeeded, entry.ReasonCode);
    }

    [Fact]
    public async Task RevokeGrantAsync_GrantNotFound_WritesDeniedAuditEvent()
    {
        string tag = $"grant-audit-notfound-{Guid.NewGuid():N}";
        string missingKey = $"{tag}-missing-key";

        await _factory.RunInScopeAsync(async sp =>
        {
            IGrantListService service = sp.GetRequiredService<IGrantListService>();
            RevokeGrantResult result = await service.RevokeGrantAsync(GrantKey.Create(missingKey));
            Assert.Equal(RevokeGrantResult.NotFound, result);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Revoke, missingKey);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task RevokeGrantsBySubjectAsync_WritesOneBoundedSummaryEvent_RegardlessOfGrantCount()
    {
        string tag = $"grant-audit-bulk-{Guid.NewGuid():N}";
        string subjectId = $"{tag}-subject";
        PersistedGrant g1 = MakeGrant($"{tag}-key-1", $"{tag}-client-1", subjectId);
        PersistedGrant g2 = MakeGrant($"{tag}-key-2", $"{tag}-client-2", subjectId);

        await _factory.RunInScopeAsync(async sp =>
        {
            PersistedGrantDbContext grantDb = sp.GetRequiredService<PersistedGrantDbContext>();
            grantDb.PersistedGrants.AddRange(g1, g2);
            await grantDb.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IGrantListService service = sp.GetRequiredService<IGrantListService>();
            int count = await service.RevokeGrantsBySubjectAsync(UserId.Create(subjectId));
            Assert.Equal(2, count);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            ApplicationDbContext db = sp.GetRequiredService<ApplicationDbContext>();
            var entries = db.AuditLogEntries
                .Where(e => e.Category == AuditCategory.Grant && e.Action == AuditAction.BulkRevoke &&
                            e.TargetId == subjectId)
                .ToList();

            // Exactly one bounded summary event, never one row per revoked grant.
            AuditLogEntry entry = Assert.Single(entries);
            Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
            Assert.Contains("2", entry.Details);
        });
    }

    [Fact]
    public async Task RevokeGrantsBySubjectAsync_ZeroGrants_StillWritesOneSucceededEvent()
    {
        string tag = $"grant-audit-bulk-zero-{Guid.NewGuid():N}";
        string subjectId = $"{tag}-subject-with-no-grants";

        await _factory.RunInScopeAsync(async sp =>
        {
            IGrantListService service = sp.GetRequiredService<IGrantListService>();
            int count = await service.RevokeGrantsBySubjectAsync(UserId.Create(subjectId));
            Assert.Equal(0, count);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.BulkRevoke, subjectId);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task RevokeGrantAsync_UnexpectedPersistenceFailure_WritesFailedAuditEventAndRethrows()
    {
        string key = $"grant-audit-failure-{Guid.NewGuid():N}";

        await Assert.ThrowsAnyAsync<Exception>(() => _factory.RunInScopeAsync(async sp =>
        {
            IGrantListService service = sp.GetRequiredService<IGrantListService>();
            await sp.GetRequiredService<PersistedGrantDbContext>().DisposeAsync();
            await service.RevokeGrantAsync(GrantKey.Create(key));
        }));

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Revoke, key);
        Assert.Equal(AuditOutcome.Failed, entry.Outcome);
        Assert.Equal(AuditReasonCode.PersistenceFailure, entry.ReasonCode);
    }
}