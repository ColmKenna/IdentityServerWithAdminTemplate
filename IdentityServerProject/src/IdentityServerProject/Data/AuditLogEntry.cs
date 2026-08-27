using IdentityServerProject.Services.AuditLogs;

namespace IdentityServerProject.Data;

public class AuditLogEntry
{
    public int Id { get; set; }

    public DateTime Timestamp { get; set; } // UTC

    public string CorrelationId { get; set; } = string.Empty;

    public string ActorSubjectId { get; set; } = string.Empty;

    public string ActorName { get; set; } = string.Empty;

    public string? IpAddress { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public AuditOutcome Outcome { get; set; }

    public string ReasonCode { get; set; } = string.Empty;

    public bool IsSuccess { get; set; }

    public string? TargetId { get; set; }

    public string? TargetName { get; set; }

    public string? OldValuesJson { get; set; }

    public string? NewValuesJson { get; set; }

    public string? Details { get; set; }
}
