using System.Text.Json;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IdentityServerProject.Services.AuditLogs;

public class AuditWriter : IAuditWriter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditWriter> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;

    public AuditWriter(
        IServiceScopeFactory scopeFactory,
        ILogger<AuditWriter> logger,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
    }

    public async Task WriteAsync(AdminAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        var context = _httpContextAccessor.HttpContext;
        var (actorSubjectId, actorName) = AuditActorResolver.Resolve(context?.User);
        var correlationId = context?.TraceIdentifier ?? string.Empty;
        var ipAddress = context?.Connection.RemoteIpAddress?.ToString();
        var timestamp = _timeProvider.GetUtcNow().UtcDateTime;

        var logLevel = auditEvent.Outcome switch
        {
            AuditOutcome.Succeeded => LogLevel.Information,
            AuditOutcome.Denied => LogLevel.Warning,
            _ => LogLevel.Error
        };

        // Log to telemetry first (Fallback Policy): the structured log is the durable
        // record if the subsequent database write below fails.
        _logger.Log(
            logLevel,
            "AUDIT [{Category}/{Action}] Outcome: {Outcome} ({ReasonCode}) | Actor: {ActorName} ({ActorSubjectId}) | Target: {TargetName} ({TargetId}) | Details: {Details}",
            auditEvent.Category, auditEvent.Action, auditEvent.Outcome, auditEvent.ReasonCode,
            actorName, actorSubjectId, auditEvent.TargetName, auditEvent.TargetId, auditEvent.Details);

        try
        {
            // A fresh scope (and hence a fresh store instance) so this write never shares a
            // connection/transaction with whatever the caller's own scope was mid-way through -
            // including a transaction that caller just rolled back.
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IAdminAuditStore>();

            var record = new AuditLogRecord(
                Timestamp: timestamp,
                CorrelationId: correlationId,
                ActorSubjectId: UserId.Create(actorSubjectId),
                ActorName: actorName,
                IpAddress: ipAddress,
                Category: auditEvent.Category,
                Action: auditEvent.Action,
                Outcome: auditEvent.Outcome,
                ReasonCode: auditEvent.ReasonCode,
                IsSuccess: auditEvent.Outcome == AuditOutcome.Succeeded,
                TargetId: auditEvent.TargetId,
                TargetName: auditEvent.TargetName,
                OldValuesJson: SerializeAuditValue(auditEvent.OldValues),
                NewValuesJson: SerializeAuditValue(auditEvent.NewValues),
                Details: auditEvent.Details);

            await store.WriteAsync(record, cancellationToken);
        }
        catch (Exception ex)
        {
            // Telemetry Fallback policy: the structured log above already captured this
            // event, so a persistence failure here must not fail the caller's mutation.
            _logger.LogError(
                "Failed to persist audit log entry for action {Category}/{Action} on {TargetName} ({ExceptionType}). The telemetry log was successfully recorded.",
                auditEvent.Category,
                auditEvent.Action,
                auditEvent.TargetName,
                ex.GetType().Name);
        }
    }

    private static string? SerializeAuditValue(object? value)
    {
        if (value == null)
            return null;

        return value is IAuditValue
            ? JsonSerializer.Serialize(value, value.GetType(), AuditJsonOptions.Default)
            : "{\"redacted\":true}";
    }
}
