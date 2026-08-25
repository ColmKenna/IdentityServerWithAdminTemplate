namespace IdentityServerProject.Services.AuditLogs;

/// <summary>
/// Structured optional filters for the read-only administration audit trail.
/// </summary>
public sealed class AuditLogFilter
{
    public string? ActorSubjectId { get; set; }

    public string? TargetId { get; set; }

    public AuditCategory? Category { get; set; }

    public AuditAction? Action { get; set; }

    public AuditOutcome? Outcome { get; set; }

    public string? CorrelationId { get; set; }
}
