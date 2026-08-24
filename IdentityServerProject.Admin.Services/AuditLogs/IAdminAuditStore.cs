using System;
using System.Threading;
using System.Threading.Tasks;

namespace IdentityServerProject.Services.AuditLogs;

/// <summary>
/// A single admin audit event ready for persistence. Host-owned equivalent of the
/// <c>AuditLogEntry</c> EF entity, kept free of any host/data-layer type so the library never
/// references it directly.
/// </summary>
public sealed record AuditLogRecord(
    DateTime Timestamp,
    string CorrelationId,
    string ActorSubjectId,
    string ActorName,
    string? IpAddress,
    string Category,
    string Action,
    AuditOutcome Outcome,
    string ReasonCode,
    bool IsSuccess,
    string? TargetId,
    string? TargetName,
    string? OldValuesJson,
    string? NewValuesJson,
    string? Details);

/// <summary>
/// Host-owned persistence for the admin audit trail: durable writes and the filtered,
/// paged reads behind the Admin &gt; Audit Log list page.
/// </summary>
public interface IAdminAuditStore
{
    Task WriteAsync(AuditLogRecord record, CancellationToken cancellationToken = default);

    Task<ListResult<AuditLogListItem>> GetEntriesAsync(
        AuditLogFilter filter,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
}
