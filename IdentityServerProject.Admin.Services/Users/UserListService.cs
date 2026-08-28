using IdentityServerProject.Services.AuditLogs;

namespace IdentityServerProject.Services.Users;

public class UserListService(
    IIdentityUserAdministrationStore store,
    IAuditWriter auditWriter) : IUserListService
{
    private readonly IAuditWriter _auditWriter = auditWriter;
    private readonly IIdentityUserAdministrationStore _store = store;

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
            (UserUnlockResult result, string targetName) = await _store.UnlockUserAsync(userId, cancellationToken);

            switch (result.Status)
            {
                case UserUnlockStatus.NotFound:
                    await _auditWriter.WriteAsync(new AdminAuditEvent(
                        AuditCategory.User, AuditAction.Unlock, AuditOutcome.Denied, AuditReasonCode.NotFound,
                        userId.Value, targetName, Details: "User not found."), cancellationToken);
                    return result;

                case UserUnlockStatus.Failed:
                    await _auditWriter.WriteAsync(new AdminAuditEvent(
                        AuditCategory.User, AuditAction.Unlock, AuditOutcome.Denied, AuditReasonCode.ValidationFailed,
                        userId.Value, targetName,
                        Details: string.Join(" ", result.Errors)), cancellationToken);
                    return result;
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.User, AuditAction.Unlock, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                userId.Value, targetName,
                Details: $"Unlocked user '{targetName}'"), cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.User, AuditAction.Unlock, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
                userId.Value, userId.Value,
                Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
            throw;
        }
    }
}