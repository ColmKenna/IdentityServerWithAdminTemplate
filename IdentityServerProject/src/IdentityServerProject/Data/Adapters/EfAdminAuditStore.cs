using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services;
using IdentityServerProject.Services.AuditLogs;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Data.Adapters;

/// <summary>
/// EF-backed <see cref="IAdminAuditStore"/> adapter: durable writes and filtered, paged reads
/// against <see cref="ApplicationDbContext"/>.
/// </summary>
public sealed class EfAdminAuditStore : IAdminAuditStore
{
    private readonly ApplicationDbContext _dbContext;

    public EfAdminAuditStore(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task WriteAsync(AuditLogRecord record, CancellationToken cancellationToken = default)
    {
        var entry = new AuditLogEntry
        {
            Timestamp = record.Timestamp,
            CorrelationId = record.CorrelationId,
            ActorSubjectId = record.ActorSubjectId,
            ActorName = record.ActorName,
            IpAddress = record.IpAddress,
            Category = record.Category.Value,
            Action = record.Action.Value,
            Outcome = record.Outcome,
            ReasonCode = record.ReasonCode.Value,
            IsSuccess = record.IsSuccess,
            TargetId = record.TargetId,
            TargetName = record.TargetName,
            OldValuesJson = record.OldValuesJson,
            NewValuesJson = record.NewValuesJson,
            Details = record.Details
        };

        _dbContext.AuditLogEntries.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ListResult<AuditLogListItem>> GetEntriesAsync(
        AuditLogFilter filter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default)
    {
        pagination = pagination.Normalize();

        var query = ApplyFilter(_dbContext.AuditLogEntries.AsNoTracking(), filter);

        var totalCount = await query.CountAsync(cancellationToken);

        var pageEntities = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        var items = pageEntities.Select(MapToListItem).ToList();

        return new ListResult<AuditLogListItem>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize,
        };
    }

    private static IQueryable<AuditLogEntry> ApplyFilter(IQueryable<AuditLogEntry> query, AuditLogFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (!string.IsNullOrWhiteSpace(filter.ActorSubjectId))
        {
            var actorSubjectId = LikeExtensions.EscapeLikePattern(filter.ActorSubjectId.Trim());
            query = query.Where(e => EF.Functions.Like(e.ActorSubjectId, $"%{actorSubjectId}%"));
        }

        if (!string.IsNullOrWhiteSpace(filter.TargetId))
        {
            var targetId = LikeExtensions.EscapeLikePattern(filter.TargetId.Trim());
            query = query.Where(e => e.TargetId != null && EF.Functions.Like(e.TargetId, $"%{targetId}%"));
        }

        if (filter.Category.HasValue && !filter.Category.Value.IsEmpty)
        {
            var category = filter.Category.Value.Value.Trim();
            query = query.Where(e => e.Category == category);
        }

        if (filter.Action.HasValue && !filter.Action.Value.IsEmpty)
        {
            var action = filter.Action.Value.Value.Trim();
            query = query.Where(e => e.Action == action);
        }

        if (filter.Outcome.HasValue)
        {
            query = query.Where(e => e.Outcome == filter.Outcome.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.CorrelationId))
        {
            var correlationId = filter.CorrelationId.Trim();
            query = query.Where(e => e.CorrelationId == correlationId);
        }

        return query;
    }

    private static AuditLogListItem MapToListItem(AuditLogEntry entity) => new()
    {
        Id = entity.Id,
        Timestamp = entity.Timestamp,
        ActorName = entity.ActorName,
        ActorSubjectId = entity.ActorSubjectId,
        Category = AuditCategory.Create(entity.Category),
        Action = AuditAction.From(entity.Action),
        Outcome = entity.Outcome,
        IsSuccess = entity.IsSuccess,
        ReasonCode = string.IsNullOrWhiteSpace(entity.ReasonCode) ? null : AuditReasonCode.From(entity.ReasonCode),
        CorrelationId = entity.CorrelationId,
        IpAddress = entity.IpAddress,
        TargetId = entity.TargetId,
        TargetName = entity.TargetName,
        Details = entity.Details,
        OldValuesJson = entity.OldValuesJson,
        NewValuesJson = entity.NewValuesJson,
    };
}
