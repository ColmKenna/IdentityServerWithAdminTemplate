using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Users;

public partial class UserDetailsService
{
    public Task<RoleChangeResult> AddRoleAsync(UserId userId, string role,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.AddRole,
            userId.Value,
            () => AddRoleCoreAsync(userId, role ?? string.Empty, cancellationToken),
            cancellationToken);

    private async Task<RoleChangeResult> AddRoleCoreAsync(UserId userId, string role,
        CancellationToken cancellationToken)
    {
        RoleAdditionOutcome outcome = await _store.AddRoleAsync(userId, role, cancellationToken);

        switch (outcome.Status)
        {
            case RoleAdditionStatus.UserNotFound:
                await AuditDeniedAsync(AuditAction.AddRole, AuditReasonCode.NotFound, userId, outcome.TargetName,
                    "User not found.", cancellationToken);
                return RoleChangeResult.Failed("User not found.");

            case RoleAdditionStatus.RoleNotFound:
                await AuditDeniedAsync(AuditAction.AddRole, AuditReasonCode.NotFound, userId, outcome.TargetName,
                    "Role not found.", cancellationToken);
                return RoleChangeResult.Failed("Role not found.");

            case RoleAdditionStatus.AlreadyMember:
                return RoleChangeResult.Succeeded();

            case RoleAdditionStatus.ValidationFailed:
                await AuditDeniedAsync(AuditAction.AddRole, AuditReasonCode.ValidationFailed, userId,
                    outcome.TargetName,
                    outcome.ErrorMessage!, cancellationToken);
                return RoleChangeResult.Failed(outcome.ErrorMessage!);
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.User, AuditAction.AddRole, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
            userId, outcome.TargetName,
            Details: $"Added role '{role}'"), cancellationToken);

        return RoleChangeResult.Succeeded();
    }

    public async Task<RoleChangeResult> RemoveRoleAsync(UserId userId, string role,
        CancellationToken cancellationToken = default)
    {
        string targetName = userId.Value;
        RoleRemovalOutcome outcome;

        try
        {
            var actorId = UserId.Create(AuditActorResolver.Resolve(_httpContextAccessor.HttpContext?.User).SubjectId);
            outcome = await _store.RemoveRoleAsync(userId, role, ProtectedAdminRoles.SysAdmin, actorId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.RemoveRole, userId.Value, targetName, ex, cancellationToken);
            throw;
        }

        targetName = outcome.TargetName;

        switch (outcome.Status)
        {
            case RoleRemovalStatus.UserNotFound:
                await AuditDeniedAsync(AuditAction.RemoveRole, AuditReasonCode.NotFound, userId.Value, targetName,
                    "User not found.", cancellationToken);
                return RoleChangeResult.Failed("User not found.", AuditReasonCode.NotFound,
                    AdminMutationStatus.NotFound);

            case RoleRemovalStatus.RoleNotFound:
                await AuditDeniedAsync(AuditAction.RemoveRole, AuditReasonCode.NotFound, userId.Value, targetName,
                    "Role not found.", cancellationToken);
                return RoleChangeResult.Failed("Role not found.", AuditReasonCode.NotFound,
                    AdminMutationStatus.NotFound);

            case RoleRemovalStatus.SelfDemotionBlocked:
                {
                    string message =
                        "You cannot remove your own SysAdmin role. Ask another administrator to do this if needed.";
                    await AuditDeniedAsync(AuditAction.RemoveRole, AuditReasonCode.SelfDemotion, userId.Value, targetName,
                        message, cancellationToken);
                    return RoleChangeResult.Failed(message, AuditReasonCode.SelfDemotion);
                }

            case RoleRemovalStatus.LastProtectedMemberBlocked:
                {
                    string message =
                        $"'{targetName}' is the last SysAdmin. Assign the role to another user before removing it here.";
                    await AuditDeniedAsync(AuditAction.RemoveRole, AuditReasonCode.LastAdministrator, userId.Value,
                        targetName,
                        message, cancellationToken);
                    return RoleChangeResult.Failed(message, AuditReasonCode.LastAdministrator);
                }

            case RoleRemovalStatus.ValidationFailed:
                await AuditDeniedAsync(AuditAction.RemoveRole, AuditReasonCode.ValidationFailed, userId.Value,
                    targetName,
                    outcome.ErrorMessage!, cancellationToken);
                return RoleChangeResult.Failed(outcome.ErrorMessage!, AuditReasonCode.ValidationFailed,
                    AdminMutationStatus.ValidationFailed);
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.User, AuditAction.RemoveRole, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                userId.Value, targetName,
                Details: outcome.RoleWasRemoved ? $"Removed role '{role}'" : $"Role '{role}' was not assigned"),
            cancellationToken);

        if (outcome.RoleWasRemoved)
            // This runs after the role transaction commits. Its own durable mutations complete
            // before any back-channel call is made.
            await RevokeUserAccessAsync(new UserActionContext(userId, null), cancellationToken);

        return RoleChangeResult.Succeeded();
    }
}