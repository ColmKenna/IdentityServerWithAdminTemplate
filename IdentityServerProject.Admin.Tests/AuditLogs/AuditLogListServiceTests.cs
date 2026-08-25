using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IdentityServerProject.Data;
using IdentityServerProject.Services;
using IdentityServerProject.Services.AuditLogs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.AuditLogs;

public class AuditLogListServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public AuditLogListServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static AuditLogEntry MakeEntry(
        string actorSubjectId,
        string action,
        string targetId = "target-1",
        string category = "Client",
        AuditOutcome outcome = AuditOutcome.Succeeded,
        string? correlationId = null,
        DateTime? timestamp = null) => new()
    {
        ActorSubjectId = actorSubjectId,
        ActorName = $"{actorSubjectId}@sales.local",
        TargetId = targetId,
        TargetName = targetId,
        Category = category,
        Action = action,
        Outcome = outcome,
        IsSuccess = outcome == AuditOutcome.Succeeded,
        CorrelationId = correlationId ?? $"corr-{Guid.NewGuid():N}",
        Timestamp = timestamp ?? DateTime.UtcNow,
    };

    private async Task SeedAsync(IEnumerable<AuditLogEntry> entries)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var dbContext = sp.GetRequiredService<ApplicationDbContext>();
            dbContext.AuditLogEntries.AddRange(entries);
            await dbContext.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GetAuditLogEntriesAsync_ReturnsEntriesOrderedByTimestampDescending()
    {
        var tag = $"audit-order-{Guid.NewGuid():N}";
        await SeedAsync(new[]
        {
            MakeEntry(tag, $"{tag}-older", timestamp: DateTime.UtcNow.AddHours(-2)),
            MakeEntry(tag, $"{tag}-newer", timestamp: DateTime.UtcNow.AddHours(-1)),
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IAuditLogListService>();
            var result = await service.GetAuditLogEntriesAsync(new AuditLogFilter { ActorSubjectId = tag }, Pagination.From(1, 10));

            Assert.Equal(new[] { $"{tag}-newer", $"{tag}-older" }, result.Items.Select(item => item.Action.Value));
        });
    }

    [Fact]
    public async Task GetAuditLogEntriesAsync_AppliesStructuredFiltersTogether()
    {
        var tag = $"audit-filter-{Guid.NewGuid():N}";
        var matching = MakeEntry(tag, "Update", $"{tag}-target", "Client", AuditOutcome.Denied, $"{tag}-correlation");
        var wrongOutcome = MakeEntry(tag, "Update", $"{tag}-target", "Client", AuditOutcome.Succeeded, $"{tag}-correlation");
        var unrelated = MakeEntry($"{tag}-other", "Delete", $"{tag}-other-target", "User", AuditOutcome.Failed, $"{tag}-other-correlation");
        await SeedAsync(new[] { matching, wrongOutcome, unrelated });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IAuditLogListService>();
            var result = await service.GetAuditLogEntriesAsync(new AuditLogFilter
            {
                ActorSubjectId = tag[..^2],
                TargetId = "target",
                Category = AuditCategory.Client,
                Action = AuditAction.Update,
                Outcome = AuditOutcome.Denied,
                CorrelationId = $"{tag}-correlation",
            }, Pagination.From(1, 10));

            Assert.Single(result.Items);
            Assert.Equal(matching.CorrelationId, result.Items[0].CorrelationId);
        });
    }

    [Fact]
    public async Task GetAuditLogEntriesAsync_UsesContainsMatchingOnlyForActorAndTarget()
    {
        var tag = $"audit-partial-{Guid.NewGuid():N}";
        await SeedAsync(new[]
        {
            MakeEntry($"{tag}-Alice-Admin", "Update", $"{tag}-target-123", "Client"),
            MakeEntry($"{tag}-other", "Update", $"{tag}-other", "Client"),
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IAuditLogListService>();
            var result = await service.GetAuditLogEntriesAsync(new AuditLogFilter
            {
                ActorSubjectId = "alice-admin",
                TargetId = "target-123",
            }, Pagination.From(1, 10));

            Assert.Single(result.Items);
        });
    }

    [Fact]
    public async Task GetAuditLogEntriesAsync_ReturnsEmptyResultWhenNoFilterMatches()
    {
        var tag = $"audit-nomatch-{Guid.NewGuid():N}";
        await SeedAsync(new[] { MakeEntry(tag, "Update", category: "Client") });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IAuditLogListService>();
            var result = await service.GetAuditLogEntriesAsync(new AuditLogFilter { Category = AuditCategory.Create($"{tag}-no-match") }, Pagination.From(1, 10));

            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
        });
    }

    [Fact]
    public async Task GetAuditLogEntriesAsync_PaginatesFilteredEntries()
    {
        var tag = $"audit-page-{Guid.NewGuid():N}";
        await SeedAsync(Enumerable.Range(1, 5)
            .Select(i => MakeEntry(tag, $"{tag}-action-{i}", timestamp: DateTime.UtcNow.AddMinutes(-i))));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IAuditLogListService>();
            var filter = new AuditLogFilter { ActorSubjectId = tag };
            var page1 = await service.GetAuditLogEntriesAsync(filter, Pagination.From(1, 2));
            var page3 = await service.GetAuditLogEntriesAsync(filter, Pagination.From(3, 2));

            Assert.Equal(2, page1.Items.Count);
            Assert.Equal(5, page1.TotalCount);
            Assert.Single(page3.Items);
            Assert.True(page3.HasPreviousPage);
            Assert.False(page3.HasNextPage);
        });
    }
}
