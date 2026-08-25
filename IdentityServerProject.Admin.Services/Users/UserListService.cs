using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.AuditLogs;

namespace IdentityServerProject.Services.Users;

public class UserListService : IUserListService
{
    private readonly IIdentityUserAdministrationStore _store;
    private readonly IAuditWriter _auditWriter;

    public UserListService(IIdentityUserAdministrationStore store, IAuditWriter auditWriter)
    {
        _store = store;
        _auditWriter = auditWriter;
    }

    public Task<ListResult<UserListItem>> GetUsersAsync(
        string? filter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default) =>
        _store.GetUsersAsync(filter, pagination, cancellationToken);

    public async Task<UserUnlockResult> UnlockUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (result, targetName) = await _store.UnlockUserAsync(userId, cancellationToken);

            switch (result.Status)
            {
                case UserUnlockStatus.NotFound:
                    await _auditWriter.WriteAsync(new AdminAuditEvent(
                        AuditCategories.User, AuditActions.Unlock, AuditOutcome.Denied, AuditReasonCodes.NotFound,
                        TargetId: userId, TargetName: targetName, Details: "User not found."), cancellationToken);
                    return result;

                case UserUnlockStatus.Failed:
                    await _auditWriter.WriteAsync(new AdminAuditEvent(
                        AuditCategories.User, AuditActions.Unlock, AuditOutcome.Denied, AuditReasonCodes.ValidationFailed,
                        TargetId: userId, TargetName: targetName,
                        Details: string.Join(" ", result.Errors)), cancellationToken);
                    return result;
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.User, AuditActions.Unlock, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: userId, TargetName: targetName,
                Details: $"Unlocked user '{targetName}'"), cancellationToken);

            return result;
        }
        catch (System.Exception ex)
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.User, AuditActions.Unlock, AuditOutcome.Failed, AuditReasonCodes.PersistenceFailure,
                TargetId: userId, TargetName: userId,
                Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
            throw;
        }
    }
}
