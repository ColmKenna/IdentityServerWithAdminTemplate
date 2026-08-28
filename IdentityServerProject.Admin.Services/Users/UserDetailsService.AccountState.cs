using IdentityServerProject.Services.AuditLogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PersistedGrant = Duende.IdentityServer.EntityFramework.Entities.PersistedGrant;

namespace IdentityServerProject.Services.Users;

public partial class UserDetailsService
{
    public Task<PasswordResetResult> ResetPasswordAsync(UserId userId, string newPassword,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.ResetPassword,
            userId.Value,
            () => ResetPasswordCoreAsync(userId, newPassword, cancellationToken),
            cancellationToken);

    private async Task<PasswordResetResult> ResetPasswordCoreAsync(UserId userId, string newPassword,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
            return await DenyPasswordRequiredAsync(userId, cancellationToken);

        PasswordResetOutcome outcome = await _store.ResetPasswordAsync(userId, newPassword, cancellationToken);
        switch (outcome.Status)
        {
            case PasswordResetStatus.UserNotFound:
                await AuditDeniedAsync(AuditAction.ResetPassword, AuditReasonCode.NotFound, userId, outcome.TargetName,
                    "User not found.", cancellationToken);
                return PasswordResetResult.Failed("User not found.");

            case PasswordResetStatus.ValidationFailed:
                await AuditDeniedAsync(AuditAction.ResetPassword, AuditReasonCode.ValidationFailed, userId,
                    outcome.TargetName,
                    outcome.ErrorMessage!, cancellationToken);
                return PasswordResetResult.Failed(outcome.ErrorMessage!);
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.User, AuditAction.ResetPassword, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
            userId, outcome.TargetName,
            Details: "Password was reset by administrator"), cancellationToken);

        return PasswordResetResult.Succeeded();
    }

    private async Task<PasswordResetResult> DenyPasswordRequiredAsync(UserId userId, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.ResetPassword, AuditReasonCode.ValidationFailed, userId, userId,
            "Password is required.", cancellationToken);
        return PasswordResetResult.Failed("Password is required.");
    }

    public Task<UserSuspendResult> SuspendUserAsync(UserActionContext context,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.SuspendUser,
            context.Target.Value,
            () => SuspendUserCoreAsync(context, cancellationToken),
            cancellationToken);

    private async Task<UserSuspendResult> SuspendUserCoreAsync(UserActionContext context,
        CancellationToken cancellationToken)
    {
        UserId userId = context.Target;
        UserSuspendOutcome outcome = await _store.SuspendUserAsync(context, cancellationToken);
        switch (outcome.Status)
        {
            case UserSuspendStatus.UserNotFound:
                await AuditDeniedAsync(AuditAction.SuspendUser, AuditReasonCode.NotFound, userId, outcome.TargetName,
                    "User not found.", cancellationToken);
                return UserSuspendResult.Failed("User not found.");

            case UserSuspendStatus.SelfActionBlocked:
                await AuditDeniedAsync(AuditAction.SuspendUser, AuditReasonCode.SelfAction, userId, outcome.TargetName,
                    "Cannot suspend your own account.", cancellationToken);
                return UserSuspendResult.Failed("You cannot suspend your own account.");

            case UserSuspendStatus.ValidationFailed:
                await AuditDeniedAsync(AuditAction.SuspendUser, AuditReasonCode.ValidationFailed, userId,
                    outcome.TargetName,
                    "Failed to suspend user.", cancellationToken);
                return UserSuspendResult.Failed("Failed to suspend user.");
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.User, AuditAction.SuspendUser, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
            userId, outcome.TargetName,
            Details: "User suspended manually by administrator"), cancellationToken);

        return UserSuspendResult.Succeeded();
    }

    public Task<UserUnlockResult> UnlockUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
        UnlockUserCoreAsync(userId, cancellationToken);

    private async Task<UserUnlockResult> UnlockUserCoreAsync(UserId userId, CancellationToken cancellationToken)
    {
        try
        {
            UserUnlockOutcome outcome = await _store.UnlockUserAsync(userId, cancellationToken);
            UserUnlockResult result = outcome.Result;
            string targetName = outcome.TargetName;

            switch (result.Status)
            {
                case UserUnlockStatus.NotFound:
                    await _auditWriter.WriteAsync(new AdminAuditEvent(
                        AuditCategory.User, AuditAction.Unlock, AuditOutcome.Denied, AuditReasonCode.NotFound,
                        userId, targetName, Details: "User not found."), cancellationToken);
                    return result;

                case UserUnlockStatus.Failed:
                    await _auditWriter.WriteAsync(new AdminAuditEvent(
                        AuditCategory.User, AuditAction.Unlock, AuditOutcome.Denied, AuditReasonCode.ValidationFailed,
                        userId, targetName,
                        Details: string.Join(" ", result.Errors)), cancellationToken);
                    return result;
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.User, AuditAction.Unlock, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                userId, targetName,
                Details: $"Unlocked user '{targetName}'"), cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.User, AuditAction.Unlock, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
                userId, userId,
                Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
            throw;
        }
    }

    public Task<UserDeleteResult> DeleteUserAsync(UserActionContext context,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.DeleteUser,
            context.Target.Value,
            () => DeleteUserCoreAsync(context, cancellationToken),
            cancellationToken);

    private async Task<UserDeleteResult> DeleteUserCoreAsync(UserActionContext context,
        CancellationToken cancellationToken)
    {
        UserId userId = context.Target;
        UserDeleteOutcome outcome = await _store.DeleteUserAsync(context, cancellationToken);
        switch (outcome.Status)
        {
            case UserDeleteStatus.UserNotFound:
                await AuditDeniedAsync(AuditAction.DeleteUser, AuditReasonCode.NotFound, userId, outcome.TargetName,
                    "User not found.", cancellationToken);
                return UserDeleteResult.Failed("User not found.");

            case UserDeleteStatus.SelfActionBlocked:
                await AuditDeniedAsync(AuditAction.DeleteUser, AuditReasonCode.SelfAction, userId, outcome.TargetName,
                    "Cannot delete your own account.", cancellationToken);
                return UserDeleteResult.Failed("You cannot delete your own account.");

            case UserDeleteStatus.ValidationFailed:
                await AuditDeniedAsync(AuditAction.DeleteUser, AuditReasonCode.ValidationFailed, userId,
                    outcome.TargetName,
                    "Failed to delete user.", cancellationToken);
                return UserDeleteResult.Failed("Failed to delete user.");
        }

        // Delete grants for the user as well.
        try
        {
            IExecutionStrategy grantStrategy = _persistedGrantDbContext.Database.CreateExecutionStrategy();
            await grantStrategy.ExecuteAsync(async () =>
            {
                _persistedGrantDbContext.ChangeTracker.Clear();
                await using IDbContextTransaction transaction =
                    await _persistedGrantDbContext.Database.BeginTransactionAsync(cancellationToken);
                List<PersistedGrant> grants = await _persistedGrantDbContext.PersistedGrants
                    .Where(g => g.SubjectId == userId)
                    .ToListAsync(cancellationToken);

                if (grants.Count > 0)
                {
                    _persistedGrantDbContext.PersistedGrants.RemoveRange(grants);
                    await _persistedGrantDbContext.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.RemoveGrantsOnUserDelete, userId, outcome.TargetName, ex,
                cancellationToken);
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.User, AuditAction.DeleteUser, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
            userId, outcome.TargetName,
            Details: "User deleted manually by administrator"), cancellationToken);

        return UserDeleteResult.Succeeded();
    }
}