using IdentityServerProject.Services.Users;

namespace IdentityServerProject.Services.AuditLogs;

/// <summary>
/// A single admin audit event ready for persistence. Host-owned equivalent of the
/// <c>AuditLogEntry</c> EF entity, kept free of any host/data-layer type so the library never
/// references it directly.
/// </summary>
public sealed record AuditLogRecord(
    DateTime Timestamp,
    string CorrelationId,
    UserId ActorSubjectId,
    string ActorName,
    string? IpAddress,
    AuditCategory Category,
    AuditAction Action,
    AuditOutcome Outcome,
    AuditReasonCode ReasonCode,
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
        Pagination pagination = default,
        CancellationToken cancellationToken = default);
}
