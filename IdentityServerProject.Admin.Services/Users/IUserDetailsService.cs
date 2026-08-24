using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Users;

public class UserClaimSummary
{
    public required string Type { get; set; }
    public required string Value { get; set; }

    /// <summary>
    /// True when this claim's type is one the admin claim editor refuses to create
    /// (see <see cref="ReservedClaimTypePolicy"/>). Such a claim predates the policy or was
    /// written outside the admin UI, and should normally be removed.
    /// </summary>
    public bool IsReserved { get; set; }
}

public class UserDetailsModel
{
    public required string Id { get; set; }
    public required string UserName { get; set; }
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public bool IsLockedOut { get; set; }
    public System.DateTimeOffset? LockoutEnd { get; set; }
    public List<string> AssignedRoles { get; set; } = new();
    public List<string> AllRoles { get; set; } = new();
    public List<UserClaimSummary> Claims { get; set; } = new();
    public int PersistedGrantCount { get; set; }

    /// <summary>
    /// True when this user is the currently-authenticated administrator viewing their own account.
    /// </summary>
    public bool IsCurrentUser { get; set; }
}

public class RoleChangeResult
{
    public bool Success { get; set; }
    public AdminMutationStatus Status { get; set; }
    public string? ReasonCode { get; set; }
    public string? ErrorMessage { get; set; }

    public static RoleChangeResult Succeeded() => new()
    {
        Success = true,
        Status = AdminMutationStatus.Succeeded,
        ReasonCode = AuditLogs.AuditReasonCodes.Succeeded
    };

    public static RoleChangeResult Failed(
        string errorMessage,
        string reasonCode = AuditLogs.AuditReasonCodes.ValidationFailed,
        AdminMutationStatus status = AdminMutationStatus.Denied) =>
        new() { Success = false, Status = status, ReasonCode = reasonCode, ErrorMessage = errorMessage };
}

public class ClaimChangeResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static ClaimChangeResult Succeeded() => new() { Success = true };

    public static ClaimChangeResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}

public class UserAccessRevokeResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? WarningMessage { get; set; }
    public int RevokedGrantCount { get; set; }

    public static UserAccessRevokeResult Succeeded(int revokedGrantCount, string? warningMessage = null) =>
        new() { Success = true, RevokedGrantCount = revokedGrantCount, WarningMessage = warningMessage };

    public static UserAccessRevokeResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}

/// <summary>
/// Manages the full account-management workspace for a single user: roles, claims, and access revocation.
/// </summary>
public interface IUserDetailsService
{
    /// <summary>
    /// Returns full workspace details for a user by ID, including guard-relevant context
    /// (whether the requesting admin is viewing their own account). Returns null if not found.
    /// </summary>
    Task<UserDetailsModel?> GetUserDetailsAsync(string userId, string? currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a role to a user. Returns Success = false if the user or role is not found.
    /// </summary>
    Task<RoleChangeResult> AddRoleAsync(string userId, string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a role from a user. The canonical current-request actor accessor prevents an
    /// administrator from removing their own SysAdmin role, and the last SysAdmin membership
    /// is protected. Ordinary roles may be removed from their final holder.
    /// </summary>
    Task<RoleChangeResult> RemoveRoleAsync(string userId, string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a claim (type/value pair) to a user. Rejected (Reserved-Claim Guard) when the type is
    /// one the framework treats as an identity or authorization claim — see
    /// <see cref="ReservedClaimTypePolicy"/>.
    /// </summary>
    Task<ClaimChangeResult> AddClaimAsync(string userId, string claimType, string claimValue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a matching claim (type/value pair) from a user. Reserved types may be removed —
    /// removal only de-escalates, and pre-existing reserved claims need a cleanup path.
    /// </summary>
    Task<ClaimChangeResult> RemoveClaimAsync(string userId, string claimType, string claimValue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a user's current access by rotating the security stamp, deleting persisted grants,
    /// and notifying affected clients. Blocked when the target is the current administrator.
    /// </summary>
    Task<UserAccessRevokeResult> RevokeUserAccessAsync(string userId, string? currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets a user's password.
    /// </summary>
    Task<PasswordResetResult> ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// Suspends a user by setting their lockout end date to the maximum value. Blocked when the target is the current administrator.
    /// </summary>
    Task<UserSuspendResult> SuspendUserAsync(string userId, string? currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlocks a locked-out user account.
    /// </summary>
    Task<UserUnlockResult> UnlockUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user account entirely. Blocked when the target is the current administrator.
    /// </summary>
    Task<UserDeleteResult> DeleteUserAsync(string userId, string? currentUserId, CancellationToken cancellationToken = default);
}

public class PasswordResetResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static PasswordResetResult Succeeded() => new() { Success = true };

    public static PasswordResetResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}

public class UserSuspendResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static UserSuspendResult Succeeded() => new() { Success = true };

    public static UserSuspendResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}

public class UserDeleteResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static UserDeleteResult Succeeded() => new() { Success = true };

    public static UserDeleteResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}
