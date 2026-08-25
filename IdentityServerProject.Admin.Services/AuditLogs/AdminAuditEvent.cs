namespace IdentityServerProject.Services.AuditLogs;

public sealed record AdminAuditEvent(
    AuditCategory Category,
    AuditAction Action,
    AuditOutcome Outcome,
    AuditReasonCode ReasonCode,
    string? TargetId = null,
    string? TargetName = null,
    object? OldValues = null,
    object? NewValues = null,
    string? Details = null);
