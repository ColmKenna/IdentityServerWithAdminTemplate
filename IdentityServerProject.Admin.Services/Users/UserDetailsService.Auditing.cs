using IdentityServerProject.Services.AuditLogs;

namespace IdentityServerProject.Services.Users;

public partial class UserDetailsService
{
    private Task AuditDeniedAsync(AuditAction action, AuditReasonCode reasonCode, string targetId, string targetName, string details, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.User, action, AuditOutcome.Denied, reasonCode,
            TargetId: targetId, TargetName: targetName, Details: details), cancellationToken);

    private async Task<T> ExecuteAuditedAsync<T>(
        AuditAction action,
        string userId,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(action, userId, userId, ex, cancellationToken);
            throw;
        }
    }

    private Task AuditFailedAsync(AuditAction action, string targetId, string targetName, Exception ex, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.User, action, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
            TargetId: targetId, TargetName: targetName, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
}
