using System;

namespace IdentityServerProject.Services.AuditLogs;

/// <summary>
/// A single row projected for display on the Admin &gt; Audit Log list page.\n/// </summary>
public sealed class AuditLogListItem
{
    public required int Id { get; init; }

    public required DateTime Timestamp { get; init; }

    public required string ActorName { get; init; }

    public string? ActorSubjectId { get; init; }

    public required AuditCategory Category { get; init; }

    public required AuditAction Action { get; init; }

    public required AuditOutcome Outcome { get; init; }

    public required bool IsSuccess { get; init; }

    public AuditReasonCode? ReasonCode { get; init; }

    public string? CorrelationId { get; init; }

    public string? IpAddress { get; init; }

    public string? TargetId { get; init; }

    public required string? TargetName { get; init; }

    public required string? Details { get; init; }

    public string? OldValuesJson { get; init; }

    public string? NewValuesJson { get; init; }
}
