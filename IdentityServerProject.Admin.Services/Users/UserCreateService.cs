using System;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.AuditLogs;

namespace IdentityServerProject.Services.Users;

public class UserCreateService : IUserCreateService
{
    private readonly IIdentityUserAdministrationStore _store;
    private readonly IAuditWriter _auditWriter;

    public UserCreateService(IIdentityUserAdministrationStore store, IAuditWriter auditWriter)
    {
        _store = store;
        _auditWriter = auditWriter;
    }

    public async Task<UserCreateResult> CreateUserAsync(UserCreateInputModel input, CancellationToken cancellationToken = default)
    {
        var userName = input.UserName?.Trim() ?? string.Empty;

        try
        {
            var outcome = await _store.CreateUserAsync(input, cancellationToken);
            var result = outcome.Result;
            var reasonCode = outcome.ReasonCode;

            if (!result.Success)
            {
                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategory.User, AuditAction.Create, AuditOutcome.Denied, reasonCode,
                    TargetId: userName, TargetName: userName,
                    Details: "User creation validation failed."), cancellationToken);

                return result;
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.User, AuditAction.Create, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: result.UserId, TargetName: userName,
                Details: $"Created user '{userName}'"), cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.User, AuditAction.Create, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
                TargetId: userName, TargetName: userName,
                Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
            throw;
        }
    }
}
