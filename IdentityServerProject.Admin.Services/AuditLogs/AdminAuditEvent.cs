namespace IdentityServerProject.Services.AuditLogs;

public sealed record AdminAuditEvent(
    string Category,
    string Action,
    AuditOutcome Outcome,
    string ReasonCode,
    string? TargetId = null,
    string? TargetName = null,
    object? OldValues = null,
    object? NewValues = null,
    string? Details = null);
