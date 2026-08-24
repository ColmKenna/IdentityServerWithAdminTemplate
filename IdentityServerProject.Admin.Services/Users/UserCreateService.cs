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
            var (result, reasonCode) = await _store.CreateUserAsync(input, cancellationToken);
            if (!result.Success)
            {
                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategories.User, AuditActions.Create, AuditOutcome.Denied, reasonCode,
                    TargetId: userName, TargetName: userName,
                    Details: "User creation validation failed."), cancellationToken);

                return result;
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.User, AuditActions.Create, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: result.UserId, TargetName: userName,
                Details: $"Created user '{userName}'"), cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.User, AuditActions.Create, AuditOutcome.Failed, AuditReasonCodes.PersistenceFailure,
                TargetId: userName, TargetName: userName,
                Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
            throw;
        }
    }
}
