using System;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.AuditLogs;

namespace IdentityServerProject.Services.Users;

public partial class UserDetailsService
{
    private Task AuditDeniedAsync(string action, string reasonCode, string targetId, string targetName, string details, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.User, action, AuditOutcome.Denied, reasonCode,
            TargetId: targetId, TargetName: targetName, Details: details), cancellationToken);

    private async Task<T> ExecuteAuditedAsync<T>(
        string action,
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

    private Task AuditFailedAsync(string action, string targetId, string targetName, Exception ex, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.User, action, AuditOutcome.Failed, AuditReasonCodes.PersistenceFailure,
            TargetId: targetId, TargetName: targetName, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
}
