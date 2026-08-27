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
        ListQuery query,
        CancellationToken cancellationToken = default) =>
        _store.GetUsersAsync(query, cancellationToken);

    public async Task<UserUnlockResult> UnlockUserAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (result, targetName) = await _store.UnlockUserAsync(userId, cancellationToken);

            switch (result.Status)
            {
                case UserUnlockStatus.NotFound:
                    await _auditWriter.WriteAsync(new AdminAuditEvent(
                        AuditCategory.User, AuditAction.Unlock, AuditOutcome.Denied, AuditReasonCode.NotFound,
                        TargetId: userId.Value, TargetName: targetName, Details: "User not found."), cancellationToken);
                    return result;

                case UserUnlockStatus.Failed:
                    await _auditWriter.WriteAsync(new AdminAuditEvent(
                        AuditCategory.User, AuditAction.Unlock, AuditOutcome.Denied, AuditReasonCode.ValidationFailed,
                        TargetId: userId.Value, TargetName: targetName,
                        Details: string.Join(" ", result.Errors)), cancellationToken);
                    return result;
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.User, AuditAction.Unlock, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: userId.Value, TargetName: targetName,
                Details: $"Unlocked user '{targetName}'"), cancellationToken);

            return result;
        }
        catch (System.Exception ex)
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.User, AuditAction.Unlock, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
                TargetId: userId.Value, TargetName: userId.Value,
                Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
            throw;
        }
    }
}
