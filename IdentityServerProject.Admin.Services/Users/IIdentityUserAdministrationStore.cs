using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IdentityServerProject.Services.Users;

/// <summary>
/// A user's roles, claims, and profile fields, projected for the Admin &gt; Users &gt; Details
/// workspace. Persisted-grant count is deliberately excluded: it comes from the Duende operational store,
/// which the library already reaches directly.
/// </summary>
public sealed record UserAccountDetails(
    UserId Id,
    string UserName,
    string? Email,
    string? FullName,
    bool IsLockedOut,
    DateTimeOffset? LockoutEnd,
    IReadOnlyList<string> AssignedRoles,
    IReadOnlyList<string> AllRoles,
    IReadOnlyList<(string Type, string Value)> Claims);

public enum RoleAdditionStatus
{
    Added,
    AlreadyMember,
    UserNotFound,
    RoleNotFound,
    ValidationFailed,
}

public sealed record RoleAdditionOutcome(RoleAdditionStatus Status, string TargetName, string? ErrorMessage);

public enum RoleRemovalStatus
{
    /// <summary>Removed, or the user was already not a member (idempotent no-op).</summary>
    Succeeded,
    UserNotFound,
    RoleNotFound,
    SelfDemotionBlocked,
    LastProtectedMemberBlocked,
    ValidationFailed,
}

public sealed record RoleRemovalOutcome(RoleRemovalStatus Status, string TargetName, bool RoleWasRemoved, string? ErrorMessage);

public enum ClaimMutationStatus
{
    Applied,
    AlreadyExists,
    UserNotFound,
    ValidationFailed,
}

public sealed record ClaimMutationOutcome(ClaimMutationStatus Status, string TargetName, string? ErrorMessage);

public enum SecurityStampRotationStatus
{
    Succeeded,
    UserNotFound,
    SelfActionBlocked,
}

public sealed record SecurityStampRotationOutcome(SecurityStampRotationStatus Status, string TargetName);

public enum PasswordResetStatus
{
    Succeeded,
    UserNotFound,
    ValidationFailed
}

public sealed record PasswordResetOutcome(PasswordResetStatus Status, string TargetName, string? ErrorMessage);

/// <summary>
/// Host-owned persistence for Identity user administration: listing, unlock, creation, role and
/// claim mutation (including the protected-admin invariants — self-demotion and last-administrator
/// protection — that must be enforced inside the same transaction as the membership check), and
/// session-revocation's security-stamp rotation. Persisted-grant deletion is deliberately excluded:
/// it is Duende operational-store data the library already reaches directly.
/// </summary>
public interface IIdentityUserAdministrationStore
{
    Task<ListResult<UserListItem>> GetUsersAsync(
        string? filter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default);

    Task<(UserUnlockResult Result, string TargetName)> UnlockUserAsync(
        UserId userId, CancellationToken cancellationToken = default);

    Task<(UserCreateResult Result, string ReasonCode)> CreateUserAsync(
        UserCreateInputModel input, CancellationToken cancellationToken = default);

    Task<UserAccountDetails?> FindUserDetailsAsync(UserId userId, CancellationToken cancellationToken = default);

    Task<RoleAdditionOutcome> AddRoleAsync(UserId userId, string role, CancellationToken cancellationToken = default);

    Task<RoleRemovalOutcome> RemoveRoleAsync(
        UserId userId,
        string role,
        string protectedRoleName,
        UserId? actingUserId,
        CancellationToken cancellationToken = default);

    Task<ClaimMutationOutcome> AddClaimAsync(
        UserId userId, string claimType, string claimValue, CancellationToken cancellationToken = default);

    Task<ClaimMutationOutcome> RemoveClaimAsync(
        UserId userId, string claimType, string claimValue, CancellationToken cancellationToken = default);

    Task<SecurityStampRotationOutcome> RotateSecurityStampAsync(
        UserId userId, UserId? actingUserId, CancellationToken cancellationToken = default);

    Task<PasswordResetOutcome> ResetPasswordAsync(
        UserId userId, string newPassword, CancellationToken cancellationToken = default);

    Task<(UserSuspendOutcome Status, string TargetName)> SuspendUserAsync(
        UserId userId, UserId? actingUserId, CancellationToken cancellationToken = default);

    Task<(UserDeleteOutcome Status, string TargetName)> DeleteUserAsync(
        UserId userId, UserId? actingUserId, CancellationToken cancellationToken = default);
}

public enum UserSuspendOutcome
{
    Succeeded,
    UserNotFound,
    SelfActionBlocked,
    ValidationFailed
}

public enum UserDeleteOutcome
{
    Succeeded,
    UserNotFound,
    SelfActionBlocked,
    ValidationFailed
}
