using System;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Data.Adapters;

/// <summary>
/// EF/ASP.NET Core Identity-backed <see cref="IIdentityUserAdministrationStore"/> adapter. Owns
/// every direct dependency on <see cref="ApplicationUser"/>/<see cref="ApplicationDbContext"/>,
/// including the transactional protected-admin invariants (self-demotion and last-administrator
/// protection) that must be evaluated atomically alongside the role-membership check they guard.
/// </summary>
public sealed class EfIdentityUserAdministrationStore : IIdentityUserAdministrationStore
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public EfIdentityUserAdministrationStore(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<ListResult<UserListItem>> GetUsersAsync(
        string? filter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default)
    {
        pagination = pagination.Normalize();

        var query = ApplyFilter(_dbContext.Users.AsNoTracking(), filter);

        var totalCount = await query.CountAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var items = await query
            .OrderBy(u => u.UserName)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(u => new UserListItem
            {
                Id = UserId.Create(u.Id),
                UserName = u.UserName ?? u.Id,
                Email = u.Email,
                FullName = u.FullName,
                IsLockedOut = u.LockoutEnd.HasValue && u.LockoutEnd.Value > now,
                LockoutEnd = u.LockoutEnd,
            })
            .ToListAsync(cancellationToken);

        return new ListResult<UserListItem>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize,
        };
    }

    public async Task<UserUnlockOutcome> UnlockUserAsync(
        UserId userId, CancellationToken cancellationToken = default)
    {
        var userIdStr = userId.Value ?? string.Empty;
        var user = await _userManager.FindByIdAsync(userIdStr);
        if (user == null)
        {
            return new UserUnlockOutcome(UserUnlockResult.NotFound, userIdStr);
        }

        var targetName = user.UserName ?? user.Id;

        var lockoutResult = await _userManager.SetLockoutEndDateAsync(user, null);
        if (!lockoutResult.Succeeded)
        {
            return new UserUnlockOutcome(UserUnlockResult.Failed(lockoutResult), targetName);
        }

        var resetResult = await _userManager.ResetAccessFailedCountAsync(user);
        if (!resetResult.Succeeded)
        {
            return new UserUnlockOutcome(UserUnlockResult.Failed(resetResult), targetName);
        }

        return new UserUnlockOutcome(UserUnlockResult.Succeeded, targetName);
    }

    public async Task<UserCreateOutcome> CreateUserAsync(
        UserCreateInputModel input, CancellationToken cancellationToken = default)
    {
        var userName = input.UserName?.Trim() ?? string.Empty;
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = input.Email.Trim(),
            FullName = string.IsNullOrWhiteSpace(input.FullName) ? null : input.FullName.Trim(),
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, input.Password);
        if (!result.Succeeded)
        {
            var reasonCode = result.Errors.Any(e => e.Code.StartsWith("Duplicate", StringComparison.Ordinal))
                ? AuditReasonCode.NameCollision
                : AuditReasonCode.ValidationFailed;
            return new UserCreateOutcome(UserCreateResult.Failed(result.Errors.Select(e => e.Description).ToList()), reasonCode);
        }

        return new UserCreateOutcome(UserCreateResult.Succeeded(user.Id), AuditReasonCode.Succeeded);
    }

    public async Task<UserAccountDetails?> FindUserDetailsAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var userIdStr = userId.Value ?? string.Empty;
        var user = await _userManager.FindByIdAsync(userIdStr);
        if (user == null)
        {
            return null;
        }

        var assignedRoles = (await _userManager.GetRolesAsync(user)).OrderBy(r => r).ToList();
        var allRoles = await _roleManager.Roles.Select(r => r.Name!).OrderBy(r => r).ToListAsync(cancellationToken);
        var claims = (await _userManager.GetClaimsAsync(user))
            .Select(c => new UserClaim(c.Type, c.Value))
            .ToList();

        return new UserAccountDetails(
            UserId.Create(user.Id),
            user.UserName ?? user.Id,
            user.Email,
            user.FullName,
            user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow,
            user.LockoutEnd,
            assignedRoles,
            allRoles,
            claims);
    }

    public async Task<RoleAdditionOutcome> AddRoleAsync(UserId userId, string role, CancellationToken cancellationToken = default)
    {
        var userIdStr = userId.Value ?? string.Empty;
        var outcome = new RoleAdditionOutcome(RoleAdditionStatus.UserNotFound, userIdStr, null);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = new RoleAdditionOutcome(RoleAdditionStatus.UserNotFound, userIdStr, null);

            var user = await _userManager.FindByIdAsync(userIdStr);
            if (user == null)
            {
                return;
            }

            var targetName = user.UserName ?? user.Id;

            if (!await _roleManager.RoleExistsAsync(role))
            {
                outcome = new RoleAdditionOutcome(RoleAdditionStatus.RoleNotFound, targetName, null);
                return;
            }

            if (await _userManager.IsInRoleAsync(user, role))
            {
                outcome = new RoleAdditionOutcome(RoleAdditionStatus.AlreadyMember, targetName, null);
                return;
            }

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var result = await _userManager.AddToRoleAsync(user, role);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                var errorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
                outcome = new RoleAdditionOutcome(RoleAdditionStatus.ValidationFailed, targetName, errorMessage);
                return;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            outcome = new RoleAdditionOutcome(RoleAdditionStatus.Added, targetName, null);
        });

        return outcome;
    }

    public async Task<RoleRemovalOutcome> RemoveRoleAsync(
        UserId userId,
        string role,
        string protectedRoleName,
        UserId? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var userIdStr = userId.Value ?? string.Empty;
        var actingUserIdStr = actingUserId?.Value;
        var outcome = new RoleRemovalOutcome(RoleRemovalStatus.UserNotFound, userIdStr, false, null);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // A retry must not reuse entities or membership state from the failed attempt.
            _dbContext.ChangeTracker.Clear();
            outcome = new RoleRemovalOutcome(RoleRemovalStatus.UserNotFound, userIdStr, false, null);

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            var user = await _userManager.FindByIdAsync(userIdStr);
            if (user == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            var targetName = user.UserName ?? user.Id;
            var roleEntity = await _roleManager.FindByNameAsync(role);
            if (roleEntity == null)
            {
                outcome = new RoleRemovalOutcome(RoleRemovalStatus.RoleNotFound, targetName, false, null);
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            var isMember = await _dbContext.UserRoles.AnyAsync(
                ur => ur.UserId == user.Id && ur.RoleId == roleEntity.Id, cancellationToken);
            if (!isMember)
            {
                outcome = new RoleRemovalOutcome(RoleRemovalStatus.Succeeded, targetName, false, null);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var isProtectedRole = string.Equals(roleEntity.Name, protectedRoleName, StringComparison.OrdinalIgnoreCase);
            if (isProtectedRole && !string.IsNullOrWhiteSpace(actingUserIdStr)
                && string.Equals(actingUserIdStr, user.Id, StringComparison.Ordinal))
            {
                outcome = new RoleRemovalOutcome(RoleRemovalStatus.SelfDemotionBlocked, targetName, false, null);
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            if (isProtectedRole)
            {
                var protectedMemberCount = await _dbContext.UserRoles
                    .CountAsync(ur => ur.RoleId == roleEntity.Id, cancellationToken);
                if (protectedMemberCount <= 1)
                {
                    outcome = new RoleRemovalOutcome(RoleRemovalStatus.LastProtectedMemberBlocked, targetName, false, null);
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }
            }

            var result = await _userManager.RemoveFromRoleAsync(user, roleEntity.Name!);
            if (!result.Succeeded)
            {
                var errorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
                outcome = new RoleRemovalOutcome(RoleRemovalStatus.ValidationFailed, targetName, false, errorMessage);
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            outcome = new RoleRemovalOutcome(RoleRemovalStatus.Succeeded, targetName, true, null);
        });

        return outcome;
    }

    public async Task<ClaimMutationOutcome> AddClaimAsync(
        UserId userId, UserClaim claim, CancellationToken cancellationToken = default)
    {
        var claimType = claim.Type ?? string.Empty;
        var claimValue = claim.Value ?? string.Empty;
        var userIdStr = userId.Value ?? string.Empty;
        var outcome = new ClaimMutationOutcome(ClaimMutationStatus.UserNotFound, userIdStr, null);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = new ClaimMutationOutcome(ClaimMutationStatus.UserNotFound, userIdStr, null);

            var user = await _userManager.FindByIdAsync(userIdStr);
            if (user == null)
            {
                return;
            }

            var targetName = user.UserName ?? user.Id;

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

            var alreadyExists = await _dbContext.UserClaims.AnyAsync(
                claim => claim.UserId == userIdStr
                    && claim.ClaimType == claimType
                    && claim.ClaimValue == claimValue,
                cancellationToken);
            if (alreadyExists)
            {
                await transaction.RollbackAsync(cancellationToken);
                outcome = new ClaimMutationOutcome(ClaimMutationStatus.AlreadyExists, targetName, null);
                return;
            }

            var result = await _userManager.AddClaimAsync(user, new Claim(claimType, claimValue));
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                var errorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
                outcome = new ClaimMutationOutcome(ClaimMutationStatus.ValidationFailed, targetName, errorMessage);
                return;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            outcome = new ClaimMutationOutcome(ClaimMutationStatus.Applied, targetName, null);
        });

        return outcome;
    }

    public async Task<ClaimMutationOutcome> RemoveClaimAsync(
        UserId userId, UserClaim claim, CancellationToken cancellationToken = default)
    {
        var claimType = claim.Type ?? string.Empty;
        var claimValue = claim.Value ?? string.Empty;
        var userIdStr = userId.Value ?? string.Empty;
        var outcome = new ClaimMutationOutcome(ClaimMutationStatus.UserNotFound, userIdStr, null);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = new ClaimMutationOutcome(ClaimMutationStatus.UserNotFound, userIdStr, null);

            var user = await _userManager.FindByIdAsync(userIdStr);
            if (user == null)
            {
                return;
            }

            var targetName = user.UserName ?? user.Id;

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var result = await _userManager.RemoveClaimAsync(user, new Claim(claimType, claimValue));
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                var errorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
                outcome = new ClaimMutationOutcome(ClaimMutationStatus.ValidationFailed, targetName, errorMessage);
                return;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            outcome = new ClaimMutationOutcome(ClaimMutationStatus.Applied, targetName, null);
        });

        return outcome;
    }

    public async Task<SecurityStampRotationOutcome> RotateSecurityStampAsync(
        UserId userId, UserId? actingUserId, CancellationToken cancellationToken = default)
    {
        var userIdStr = userId.Value ?? string.Empty;
        var actingUserIdStr = actingUserId?.Value;
        var outcome = new SecurityStampRotationOutcome(SecurityStampRotationStatus.UserNotFound, userIdStr);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = new SecurityStampRotationOutcome(SecurityStampRotationStatus.UserNotFound, userIdStr);

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var user = await _userManager.FindByIdAsync(userIdStr);
            if (user == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            var targetName = user.UserName ?? user.Id;
            if (!string.IsNullOrWhiteSpace(actingUserIdStr) && string.Equals(actingUserIdStr, user.Id, StringComparison.Ordinal))
            {
                outcome = new SecurityStampRotationOutcome(SecurityStampRotationStatus.SelfActionBlocked, targetName);
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            var stampResult = await _userManager.UpdateSecurityStampAsync(user);
            if (!stampResult.Succeeded)
            {
                throw new InvalidOperationException("The user's security stamp could not be updated.");
            }

            await transaction.CommitAsync(cancellationToken);
            outcome = new SecurityStampRotationOutcome(SecurityStampRotationStatus.Succeeded, targetName);
        });

        return outcome;
    }

    public async Task<PasswordResetOutcome> ResetPasswordAsync(
        UserId userId, string newPassword, CancellationToken cancellationToken = default)
    {
        var userIdStr = userId.Value ?? string.Empty;
        var outcome = new PasswordResetOutcome(PasswordResetStatus.UserNotFound, userIdStr, null);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = new PasswordResetOutcome(PasswordResetStatus.UserNotFound, userIdStr, null);

            var user = await _userManager.FindByIdAsync(userIdStr);
            if (user == null)
            {
                return;
            }

            var targetName = user.UserName ?? user.Id;

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                var errorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
                outcome = new PasswordResetOutcome(PasswordResetStatus.ValidationFailed, targetName, errorMessage);
                return;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            outcome = new PasswordResetOutcome(PasswordResetStatus.Succeeded, targetName, null);
        });

        return outcome;
    }

    public async Task<UserSuspendOutcome> SuspendUserAsync(
        UserId userId, UserId? actingUserId, CancellationToken cancellationToken = default)
    {
        var userIdStr = userId.Value ?? string.Empty;
        var actingUserIdStr = actingUserId?.Value;
        var outcome = new UserSuspendOutcome(UserSuspendStatus.UserNotFound, userIdStr);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = new UserSuspendOutcome(UserSuspendStatus.UserNotFound, userIdStr);

            var user = await _userManager.FindByIdAsync(userIdStr);
            if (user == null)
            {
                return;
            }

            var targetName = user.UserName ?? user.Id;

            if (!string.IsNullOrWhiteSpace(actingUserIdStr) && string.Equals(actingUserIdStr, user.Id, StringComparison.Ordinal))
            {
                outcome = new UserSuspendOutcome(UserSuspendStatus.SelfActionBlocked, targetName);
                return;
            }

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var result = await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                outcome = new UserSuspendOutcome(UserSuspendStatus.ValidationFailed, targetName);
                return;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            outcome = new UserSuspendOutcome(UserSuspendStatus.Succeeded, targetName);
        });

        return outcome;
    }

    public async Task<UserDeleteOutcome> DeleteUserAsync(
        UserId userId, UserId? actingUserId, CancellationToken cancellationToken = default)
    {
        var userIdStr = userId.Value ?? string.Empty;
        var actingUserIdStr = actingUserId?.Value;
        var outcome = new UserDeleteOutcome(UserDeleteStatus.UserNotFound, userIdStr);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = new UserDeleteOutcome(UserDeleteStatus.UserNotFound, userIdStr);

            var user = await _userManager.FindByIdAsync(userIdStr);
            if (user == null)
            {
                return;
            }

            var targetName = user.UserName ?? user.Id;

            if (!string.IsNullOrWhiteSpace(actingUserIdStr) && string.Equals(actingUserIdStr, user.Id, StringComparison.Ordinal))
            {
                outcome = new UserDeleteOutcome(UserDeleteStatus.SelfActionBlocked, targetName);
                return;
            }

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                outcome = new UserDeleteOutcome(UserDeleteStatus.ValidationFailed, targetName);
                return;
            }

            // Note: Since DeleteAsync doesn't always automatically persist everything depending on how UserManager is configured, 
            // ensure DB is saved.
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            outcome = new UserDeleteOutcome(UserDeleteStatus.Succeeded, targetName);
        });

        return outcome;
    }

    private static IQueryable<ApplicationUser> ApplyFilter(IQueryable<ApplicationUser> query, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return query;
        }

        var escaped = LikeExtensions.EscapeLikePattern(filter.Trim().ToUpperInvariant());
        var pattern = $"%{escaped}%";

        return query.Where(u =>
            (u.NormalizedUserName != null && EF.Functions.Like(u.NormalizedUserName, pattern)) ||
            (u.NormalizedEmail != null && EF.Functions.Like(u.NormalizedEmail, pattern)) ||
            (u.FullName != null && EF.Functions.Like(u.FullName.ToUpper(), pattern)));
    }
}
