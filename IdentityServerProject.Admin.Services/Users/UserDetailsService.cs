using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.Users;

public partial class UserDetailsService : IUserDetailsService
{
    private readonly IIdentityUserAdministrationStore _store;
    private readonly PersistedGrantDbContext _persistedGrantDbContext;
    private readonly ReservedClaimTypePolicy _reservedClaimTypes;
    private readonly IBackChannelLogoutService _backChannelLogoutService;
    private readonly IAuditWriter _auditWriter;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserDetailsService(
        IIdentityUserAdministrationStore store,
        PersistedGrantDbContext persistedGrantDbContext,
        ReservedClaimTypePolicy reservedClaimTypes,
        IBackChannelLogoutService backChannelLogoutService,
        IAuditWriter auditWriter,
        IHttpContextAccessor httpContextAccessor)
    {
        _store = store;
        _persistedGrantDbContext = persistedGrantDbContext;
        _reservedClaimTypes = reservedClaimTypes;
        _backChannelLogoutService = backChannelLogoutService;
        _auditWriter = auditWriter;
        _httpContextAccessor = httpContextAccessor;
    }

    #region Profile

    public async Task<UserDetailsModel?> GetUserDetailsAsync(UserId userId, UserId? currentUserId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return null;
        }

        var account = await _store.FindUserDetailsAsync(userId, cancellationToken);
        if (account == null)
        {
            return null;
        }

        var claims = account.Claims
            .Select(c => new UserClaimSummary
            {
                Type = c.Type,
                Value = c.Value,
                IsReserved = _reservedClaimTypes.IsReserved(c.Type)
            })
            .OrderBy(c => c.Type).ThenBy(c => c.Value)
            .ToList();

        var persistedGrantCount = await _persistedGrantDbContext.PersistedGrants
            .AsNoTracking()
            .CountAsync(g => g.SubjectId == userId.Value, cancellationToken);

        return new UserDetailsModel
        {
            Id = account.Id,
            UserName = account.UserName,
            Email = account.Email,
            FullName = account.FullName,
            IsLockedOut = account.IsLockedOut,
            LockoutEnd = account.LockoutEnd,
            AssignedRoles = account.AssignedRoles.ToList(),
            AllRoles = account.AllRoles.ToList(),
            Claims = claims,
            PersistedGrantCount = persistedGrantCount,
            IsCurrentUser = currentUserId is { IsEmpty: false } && currentUserId.Value == account.Id
        };
    }

    #endregion

    #region Roles

    public Task<RoleChangeResult> AddRoleAsync(UserId userId, string role, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.AddRole,
            userId.Value,
            () => AddRoleCoreAsync(userId.Value, role ?? string.Empty, cancellationToken),
            cancellationToken);

    private async Task<RoleChangeResult> AddRoleCoreAsync(string userId, string role, CancellationToken cancellationToken)
    {
        var outcome = await _store.AddRoleAsync(userId, role, cancellationToken);

        switch (outcome.Status)
        {
            case RoleAdditionStatus.UserNotFound:
                await AuditDeniedAsync(AuditActions.AddRole, AuditReasonCodes.NotFound, userId, outcome.TargetName,
                    "User not found.", cancellationToken);
                return RoleChangeResult.Failed("User not found.");

            case RoleAdditionStatus.RoleNotFound:
                await AuditDeniedAsync(AuditActions.AddRole, AuditReasonCodes.NotFound, userId, outcome.TargetName,
                    "Role not found.", cancellationToken);
                return RoleChangeResult.Failed("Role not found.");

            case RoleAdditionStatus.AlreadyMember:
                return RoleChangeResult.Succeeded();

            case RoleAdditionStatus.ValidationFailed:
                await AuditDeniedAsync(AuditActions.AddRole, AuditReasonCodes.ValidationFailed, userId, outcome.TargetName,
                    outcome.ErrorMessage!, cancellationToken);
                return RoleChangeResult.Failed(outcome.ErrorMessage!);
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.User, AuditActions.AddRole, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
            TargetId: userId, TargetName: outcome.TargetName,
            Details: $"Added role '{role}'"), cancellationToken);

        return RoleChangeResult.Succeeded();
    }

    public async Task<RoleChangeResult> RemoveRoleAsync(UserId userId, string role, CancellationToken cancellationToken = default)
    {
        var targetName = userId.Value;
        RoleRemovalOutcome outcome;

        try
        {
            var actorId = AuditActorResolver.Resolve(_httpContextAccessor.HttpContext?.User).SubjectId;
            outcome = await _store.RemoveRoleAsync(userId, role, ProtectedAdminRoles.SysAdmin, actorId, cancellationToken);
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.RemoveRole, userId.Value, targetName, ex, cancellationToken);
            throw;
        }

        targetName = outcome.TargetName;

        switch (outcome.Status)
        {
            case RoleRemovalStatus.UserNotFound:
                await AuditDeniedAsync(AuditActions.RemoveRole, AuditReasonCodes.NotFound, userId.Value, targetName,
                    "User not found.", cancellationToken);
                return RoleChangeResult.Failed("User not found.", AuditReasonCodes.NotFound, AdminMutationStatus.NotFound);

            case RoleRemovalStatus.RoleNotFound:
                await AuditDeniedAsync(AuditActions.RemoveRole, AuditReasonCodes.NotFound, userId.Value, targetName,
                    "Role not found.", cancellationToken);
                return RoleChangeResult.Failed("Role not found.", AuditReasonCodes.NotFound, AdminMutationStatus.NotFound);

            case RoleRemovalStatus.SelfDemotionBlocked:
            {
                var message = "You cannot remove your own SysAdmin role. Ask another administrator to do this if needed.";
                await AuditDeniedAsync(AuditActions.RemoveRole, AuditReasonCodes.SelfDemotion, userId.Value, targetName,
                    message, cancellationToken);
                return RoleChangeResult.Failed(message, AuditReasonCodes.SelfDemotion);
            }

            case RoleRemovalStatus.LastProtectedMemberBlocked:
            {
                var message = $"'{targetName}' is the last SysAdmin. Assign the role to another user before removing it here.";
                await AuditDeniedAsync(AuditActions.RemoveRole, AuditReasonCodes.LastAdministrator, userId.Value, targetName,
                    message, cancellationToken);
                return RoleChangeResult.Failed(message, AuditReasonCodes.LastAdministrator);
            }

            case RoleRemovalStatus.ValidationFailed:
                await AuditDeniedAsync(AuditActions.RemoveRole, AuditReasonCodes.ValidationFailed, userId.Value, targetName,
                    outcome.ErrorMessage!, cancellationToken);
                return RoleChangeResult.Failed(outcome.ErrorMessage!, AuditReasonCodes.ValidationFailed, AdminMutationStatus.ValidationFailed);
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.User, AuditActions.RemoveRole, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
            TargetId: userId.Value, TargetName: targetName,
            Details: outcome.RoleWasRemoved ? $"Removed role '{role}'" : $"Role '{role}' was not assigned"), cancellationToken);

        if (outcome.RoleWasRemoved)
        {
            // This runs after the role transaction commits. Its own durable mutations complete
            // before any back-channel call is made.
            await RevokeUserAccessAsync(userId, null, cancellationToken);
        }

        return RoleChangeResult.Succeeded();
    }

    #endregion

    #region Claims

    public Task<ClaimChangeResult> AddClaimAsync(UserId userId, string claimType, string claimValue, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.AddClaim,
            userId.Value,
            () => AddClaimCoreAsync(userId.Value, claimType ?? string.Empty, claimValue ?? string.Empty, cancellationToken),
            cancellationToken);

    private async Task<ClaimChangeResult> AddClaimCoreAsync(string userId, string claimType, string claimValue, CancellationToken cancellationToken)
    {
        var type = ReservedClaimTypePolicy.Normalize(claimType);
        if (type.Length == 0)
        {
            await AuditDeniedAsync(AuditActions.AddClaim, AuditReasonCodes.ValidationFailed, userId, userId,
                "Claim type is required.", cancellationToken);
            return ClaimChangeResult.Failed("Claim type is required.");
        }

        if (type.Length > ValidationConstants.MaxClaimTypeLength)
        {
            var message = $"Claim type cannot exceed {ValidationConstants.MaxClaimTypeLength} characters.";
            await AuditDeniedAsync(AuditActions.AddClaim, AuditReasonCodes.ValidationFailed, userId, userId,
                message, cancellationToken);
            return ClaimChangeResult.Failed(message);
        }

        if (string.IsNullOrWhiteSpace(claimValue))
        {
            const string message = "Claim value is required.";
            await AuditDeniedAsync(AuditActions.AddClaim, AuditReasonCodes.ValidationFailed, userId, userId,
                message, cancellationToken);
            return ClaimChangeResult.Failed(message);
        }

        if (claimValue.Length > ValidationConstants.MaxClaimValueLength)
        {
            var message = $"Claim value cannot exceed {ValidationConstants.MaxClaimValueLength} characters.";
            await AuditDeniedAsync(AuditActions.AddClaim, AuditReasonCodes.ValidationFailed, userId, userId,
                message, cancellationToken);
            return ClaimChangeResult.Failed(message);
        }

        // Reserved-Claim Guard. A stored claim of a framework-owned type is copied onto the
        // signed-in principal verbatim, so allowing free-text types here would let an
        // administrator mint a role grant that no role-based check can see. See
        // ReservedClaimTypePolicy for the full reasoning.
        if (_reservedClaimTypes.IsReserved(type))
        {
            var message = $"'{type}' is a reserved claim type and cannot be assigned here. Roles are granted on the Roles tab; " +
                "identity and security claims are issued by the framework.";
            await AuditDeniedAsync(AuditActions.AddClaim, AuditReasonCodes.ReservedClaimType, userId, userId,
                message, cancellationToken);
            return ClaimChangeResult.Failed(message);
        }

        var outcome = await _store.AddClaimAsync(userId, type, claimValue ?? string.Empty, cancellationToken);
        switch (outcome.Status)
        {
            case ClaimMutationStatus.AlreadyExists:
            {
                const string message = "This exact claim is already assigned to the user.";
                await AuditDeniedAsync(AuditActions.AddClaim, AuditReasonCodes.ValidationFailed, userId, outcome.TargetName,
                    message, cancellationToken);
                return ClaimChangeResult.Failed(message);
            }

            case ClaimMutationStatus.UserNotFound:
                await AuditDeniedAsync(AuditActions.AddClaim, AuditReasonCodes.NotFound, userId, outcome.TargetName,
                    "User not found.", cancellationToken);
                return ClaimChangeResult.Failed("User not found.");

            case ClaimMutationStatus.ValidationFailed:
                await AuditDeniedAsync(AuditActions.AddClaim, AuditReasonCodes.ValidationFailed, userId, outcome.TargetName,
                    outcome.ErrorMessage!, cancellationToken);
                return ClaimChangeResult.Failed(outcome.ErrorMessage!);
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.User, AuditActions.AddClaim, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
            TargetId: userId, TargetName: outcome.TargetName,
            Details: $"Added claim type '{type}'"), cancellationToken);

        return ClaimChangeResult.Succeeded();
    }

    public Task<ClaimChangeResult> RemoveClaimAsync(UserId userId, string claimType, string claimValue, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.RemoveClaim,
            userId.Value,
            () => RemoveClaimCoreAsync(userId.Value, claimType ?? string.Empty, claimValue ?? string.Empty, cancellationToken),
            cancellationToken);

    private async Task<ClaimChangeResult> RemoveClaimCoreAsync(string userId, string claimType, string claimValue, CancellationToken cancellationToken)
    {
        var type = ReservedClaimTypePolicy.Normalize(claimType);
        if (type.Length == 0)
        {
            await AuditDeniedAsync(AuditActions.RemoveClaim, AuditReasonCodes.ValidationFailed, userId, userId,
                "Claim type is required.", cancellationToken);
            return ClaimChangeResult.Failed("Claim type is required.");
        }

        // Deliberately not gated by the Reserved-Claim Guard: removing a claim only ever
        // de-escalates, and reserved claims written before this policy existed need a way out.
        var outcome = await _store.RemoveClaimAsync(userId, type, claimValue ?? string.Empty, cancellationToken);
        switch (outcome.Status)
        {
            case ClaimMutationStatus.UserNotFound:
                await AuditDeniedAsync(AuditActions.RemoveClaim, AuditReasonCodes.NotFound, userId, outcome.TargetName,
                    "User not found.", cancellationToken);
                return ClaimChangeResult.Failed("User not found.");

            case ClaimMutationStatus.ValidationFailed:
                await AuditDeniedAsync(AuditActions.RemoveClaim, AuditReasonCodes.ValidationFailed, userId, outcome.TargetName,
                    outcome.ErrorMessage!, cancellationToken);
                return ClaimChangeResult.Failed(outcome.ErrorMessage!);
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.User, AuditActions.RemoveClaim, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
            TargetId: userId, TargetName: outcome.TargetName,
            Details: $"Removed claim type '{type}'"), cancellationToken);

        return ClaimChangeResult.Succeeded();
    }

    #endregion

    #region Access revocation

    public Task<UserAccessRevokeResult> RevokeUserAccessAsync(UserId userId, UserId? currentUserId, CancellationToken cancellationToken = default) =>
        RevokeUserAccessCoreAsync(userId.Value, currentUserId?.Value, cancellationToken);

    private async Task<UserAccessRevokeResult> RevokeUserAccessCoreAsync(string userId, string? currentUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            await AuditDeniedAsync(AuditActions.RevokeUserAccess, AuditReasonCodes.NotFound, userId, userId,
                "User not found.", cancellationToken);
            return UserAccessRevokeResult.Failed("User not found.");
        }

        var targetName = userId;
        var revokedCount = 0;
        var clientIds = new List<string>();

        try
        {
            var rotation = await _store.RotateSecurityStampAsync(userId, currentUserId, cancellationToken);
            targetName = rotation.TargetName;

            if (rotation.Status == SecurityStampRotationStatus.UserNotFound)
            {
                await AuditDeniedAsync(AuditActions.RevokeUserAccess, AuditReasonCodes.NotFound, userId, userId,
                    "User not found.", cancellationToken);
                return UserAccessRevokeResult.Failed("User not found.");
            }

            if (rotation.Status == SecurityStampRotationStatus.SelfActionBlocked)
            {
                var message = "You cannot revoke your own access from this page. Ask another administrator to do this if needed.";
                await AuditDeniedAsync(AuditActions.RevokeUserAccess, AuditReasonCodes.SelfAction, userId, targetName,
                    message, cancellationToken);
                return UserAccessRevokeResult.Failed(message);
            }

            var grantStrategy = _persistedGrantDbContext.Database.CreateExecutionStrategy();
            await grantStrategy.ExecuteAsync(async () =>
            {
                _persistedGrantDbContext.ChangeTracker.Clear();
                await using var transaction = await _persistedGrantDbContext.Database.BeginTransactionAsync(cancellationToken);
                var grants = await _persistedGrantDbContext.PersistedGrants
                    .Where(g => g.SubjectId == userId)
                    .ToListAsync(cancellationToken);

                revokedCount = grants.Count;
                clientIds = grants
                    .Where(g => !string.IsNullOrWhiteSpace(g.ClientId))
                    .Select(g => g.ClientId!)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                _persistedGrantDbContext.PersistedGrants.RemoveRange(grants);
                await _persistedGrantDbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.User, AuditActions.RevokeUserAccess, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: userId, TargetName: targetName,
                Details: $"Rotated the security stamp and revoked {revokedCount} persisted grant(s)"), cancellationToken);
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.RevokeUserAccess, userId, targetName, ex, cancellationToken);
            throw;
        }

        // External I/O is deliberately outside both database transactions. A failed client
        // notification cannot roll back the committed security-stamp rotation or grant deletion.
        string? warningMessage = null;
        try
        {
            await _backChannelLogoutService.SendLogoutNotificationsAsync(new LogoutNotificationContext
            {
                SubjectId = userId,
                ClientIds = clientIds
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.User, AuditActions.SendBackChannelLogout, AuditOutcome.Failed,
                AuditReasonCodes.NotificationFailure, TargetId: userId, TargetName: targetName,
                Details: $"Back-channel logout notification failed ({ex.GetType().Name})"), cancellationToken);
            warningMessage = "Access was revoked locally, but one or more clients could not be notified.";
        }

        return UserAccessRevokeResult.Succeeded(revokedCount, warningMessage);
    }

    #endregion

    #region Account state and deletion

    public Task<PasswordResetResult> ResetPasswordAsync(UserId userId, string newPassword, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.ResetPassword,
            userId.Value,
            () => ResetPasswordCoreAsync(userId.Value, newPassword, cancellationToken),
            cancellationToken);

    private async Task<PasswordResetResult> ResetPasswordCoreAsync(string userId, string newPassword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            await AuditDeniedAsync(AuditActions.ResetPassword, AuditReasonCodes.ValidationFailed, userId, userId,
                "Password is required.", cancellationToken);
            return PasswordResetResult.Failed("Password is required.");
        }

        var outcome = await _store.ResetPasswordAsync(userId, newPassword, cancellationToken);
        switch (outcome.Status)
        {
            case PasswordResetStatus.UserNotFound:
                await AuditDeniedAsync(AuditActions.ResetPassword, AuditReasonCodes.NotFound, userId, outcome.TargetName,
                    "User not found.", cancellationToken);
                return PasswordResetResult.Failed("User not found.");

            case PasswordResetStatus.ValidationFailed:
                await AuditDeniedAsync(AuditActions.ResetPassword, AuditReasonCodes.ValidationFailed, userId, outcome.TargetName,
                    outcome.ErrorMessage!, cancellationToken);
                return PasswordResetResult.Failed(outcome.ErrorMessage!);
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.User, AuditActions.ResetPassword, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
            TargetId: userId, TargetName: outcome.TargetName,
            Details: "Password was reset by administrator"), cancellationToken);

        return PasswordResetResult.Succeeded();
    }

    public Task<UserSuspendResult> SuspendUserAsync(UserId userId, UserId? currentUserId, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.SuspendUser,
            userId.Value,
            () => SuspendUserCoreAsync(userId.Value, currentUserId?.Value, cancellationToken),
            cancellationToken);

    private async Task<UserSuspendResult> SuspendUserCoreAsync(string userId, string? currentUserId, CancellationToken cancellationToken)
    {
        var outcome = await _store.SuspendUserAsync(userId, currentUserId, cancellationToken);
        switch (outcome.Status)
        {
            case UserSuspendStatus.UserNotFound:
                await AuditDeniedAsync(AuditActions.SuspendUser, AuditReasonCodes.NotFound, userId, outcome.TargetName,
                    "User not found.", cancellationToken);
                return UserSuspendResult.Failed("User not found.");

            case UserSuspendStatus.SelfActionBlocked:
                await AuditDeniedAsync(AuditActions.SuspendUser, AuditReasonCodes.SelfAction, userId, outcome.TargetName,
                    "Cannot suspend your own account.", cancellationToken);
                return UserSuspendResult.Failed("You cannot suspend your own account.");

            case UserSuspendStatus.ValidationFailed:
                await AuditDeniedAsync(AuditActions.SuspendUser, AuditReasonCodes.ValidationFailed, userId, outcome.TargetName,
                    "Failed to suspend user.", cancellationToken);
                return UserSuspendResult.Failed("Failed to suspend user.");
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.User, AuditActions.SuspendUser, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
            TargetId: userId, TargetName: outcome.TargetName,
            Details: "User suspended manually by administrator"), cancellationToken);

        return UserSuspendResult.Succeeded();
    }

    public Task<UserUnlockResult> UnlockUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
        UnlockUserCoreAsync(userId.Value, cancellationToken);

    private async Task<UserUnlockResult> UnlockUserCoreAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await _store.UnlockUserAsync(userId, cancellationToken);
            var result = outcome.Result;
            var targetName = outcome.TargetName;

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
        catch (Exception ex)
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.User, AuditActions.Unlock, AuditOutcome.Failed, AuditReasonCodes.PersistenceFailure,
                TargetId: userId, TargetName: userId,
                Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
            throw;
        }
    }

    public Task<UserDeleteResult> DeleteUserAsync(UserId userId, UserId? currentUserId, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.DeleteUser,
            userId.Value,
            () => DeleteUserCoreAsync(userId.Value, currentUserId?.Value, cancellationToken),
            cancellationToken);

    private async Task<UserDeleteResult> DeleteUserCoreAsync(string userId, string? currentUserId, CancellationToken cancellationToken)
    {
        var outcome = await _store.DeleteUserAsync(userId, currentUserId, cancellationToken);
        switch (outcome.Status)
        {
            case UserDeleteStatus.UserNotFound:
                await AuditDeniedAsync(AuditActions.DeleteUser, AuditReasonCodes.NotFound, userId, outcome.TargetName,
                    "User not found.", cancellationToken);
                return UserDeleteResult.Failed("User not found.");

            case UserDeleteStatus.SelfActionBlocked:
                await AuditDeniedAsync(AuditActions.DeleteUser, AuditReasonCodes.SelfAction, userId, outcome.TargetName,
                    "Cannot delete your own account.", cancellationToken);
                return UserDeleteResult.Failed("You cannot delete your own account.");

            case UserDeleteStatus.ValidationFailed:
                await AuditDeniedAsync(AuditActions.DeleteUser, AuditReasonCodes.ValidationFailed, userId, outcome.TargetName,
                    "Failed to delete user.", cancellationToken);
                return UserDeleteResult.Failed("Failed to delete user.");
        }

        // Delete grants for the user as well.
        try
        {
            var grantStrategy = _persistedGrantDbContext.Database.CreateExecutionStrategy();
            await grantStrategy.ExecuteAsync(async () =>
            {
                _persistedGrantDbContext.ChangeTracker.Clear();
                await using var transaction = await _persistedGrantDbContext.Database.BeginTransactionAsync(cancellationToken);
                var grants = await _persistedGrantDbContext.PersistedGrants
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
            await AuditFailedAsync(AuditActions.RemoveGrantsOnUserDelete, userId, outcome.TargetName, ex, cancellationToken);
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.User, AuditActions.DeleteUser, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
            TargetId: userId, TargetName: outcome.TargetName,
            Details: "User deleted manually by administrator"), cancellationToken);

        return UserDeleteResult.Succeeded();
    }

    #endregion

}
