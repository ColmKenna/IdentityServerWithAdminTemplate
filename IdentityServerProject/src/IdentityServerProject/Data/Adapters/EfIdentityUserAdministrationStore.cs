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
                Id = u.Id,
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

    public async Task<(UserUnlockResult Result, string TargetName)> UnlockUserAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return (UserUnlockResult.NotFound, userId);
        }

        var targetName = user.UserName ?? user.Id;

        var lockoutResult = await _userManager.SetLockoutEndDateAsync(user, null);
        if (!lockoutResult.Succeeded)
        {
            return (UserUnlockResult.Failed(lockoutResult), targetName);
        }

        var resetResult = await _userManager.ResetAccessFailedCountAsync(user);
        if (!resetResult.Succeeded)
        {
            return (UserUnlockResult.Failed(resetResult), targetName);
        }

        return (UserUnlockResult.Succeeded, targetName);
    }

    public async Task<(UserCreateResult Result, string ReasonCode)> CreateUserAsync(
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
                ? AuditReasonCodes.NameCollision
                : AuditReasonCodes.ValidationFailed;
            return (UserCreateResult.Failed(result.Errors.Select(e => e.Description).ToList()), reasonCode);
        }

        return (UserCreateResult.Succeeded(user.Id), AuditReasonCodes.Succeeded);
    }

    public async Task<UserAccountDetails?> FindUserDetailsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return null;
        }

        var assignedRoles = (await _userManager.GetRolesAsync(user)).OrderBy(r => r).ToList();
        var allRoles = await _roleManager.Roles.Select(r => r.Name!).OrderBy(r => r).ToListAsync(cancellationToken);
        var claims = (await _userManager.GetClaimsAsync(user))
            .Select(c => (c.Type, c.Value))
            .ToList();

        return new UserAccountDetails(
            user.Id,
            user.UserName ?? user.Id,
            user.Email,
            user.FullName,
            user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow,
            user.LockoutEnd,
            assignedRoles,
            allRoles,
            claims);
    }

    public async Task<RoleAdditionOutcome> AddRoleAsync(string userId, string role, CancellationToken cancellationToken = default)
    {
        var outcome = new RoleAdditionOutcome(RoleAdditionStatus.UserNotFound, userId, null);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = new RoleAdditionOutcome(RoleAdditionStatus.UserNotFound, userId, null);

            var user = await _userManager.FindByIdAsync(userId);
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
        string userId,
        string role,
        string protectedRoleName,
        string? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var outcome = new RoleRemovalOutcome(RoleRemovalStatus.UserNotFound, userId, false, null);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // A retry must not reuse entities or membership state from the failed attempt.
            _dbContext.ChangeTracker.Clear();
            outcome = new RoleRemovalOutcome(RoleRemovalStatus.UserNotFound, userId, false, null);

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            var user = await _userManager.FindByIdAsync(userId);
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
            if (isProtectedRole && !string.IsNullOrWhiteSpace(actingUserId)
                && string.Equals(actingUserId, user.Id, StringComparison.Ordinal))
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
        string userId, string claimType, string claimValue, CancellationToken cancellationToken = default)
    {
        var outcome = new ClaimMutationOutcome(ClaimMutationStatus.UserNotFound, userId, null);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = new ClaimMutationOutcome(ClaimMutationStatus.UserNotFound, userId, null);

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return;
            }

            var targetName = user.UserName ?? user.Id;

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

            var alreadyExists = await _dbContext.UserClaims.AnyAsync(
                claim => claim.UserId == userId
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
        string userId, string claimType, string claimValue, CancellationToken cancellationToken = default)
    {
        var outcome = new ClaimMutationOutcome(ClaimMutationStatus.UserNotFound, userId, null);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = new ClaimMutationOutcome(ClaimMutationStatus.UserNotFound, userId, null);

            var user = await _userManager.FindByIdAsync(userId);
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
        string userId, string? actingUserId, CancellationToken cancellationToken = default)
    {
        var outcome = new SecurityStampRotationOutcome(SecurityStampRotationStatus.UserNotFound, userId);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = new SecurityStampRotationOutcome(SecurityStampRotationStatus.UserNotFound, userId);

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            var targetName = user.UserName ?? user.Id;
            if (!string.IsNullOrWhiteSpace(actingUserId) && string.Equals(actingUserId, user.Id, StringComparison.Ordinal))
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
        string userId, string newPassword, CancellationToken cancellationToken = default)
    {
        var outcome = new PasswordResetOutcome(PasswordResetStatus.UserNotFound, userId, null);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = new PasswordResetOutcome(PasswordResetStatus.UserNotFound, userId, null);

            var user = await _userManager.FindByIdAsync(userId);
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

    public async Task<(UserSuspendOutcome Status, string TargetName)> SuspendUserAsync(
        string userId, string? actingUserId, CancellationToken cancellationToken = default)
    {
        var outcome = (Status: UserSuspendOutcome.UserNotFound, TargetName: userId);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = (UserSuspendOutcome.UserNotFound, userId);

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return;
            }

            var targetName = user.UserName ?? user.Id;

            if (!string.IsNullOrWhiteSpace(actingUserId) && string.Equals(actingUserId, user.Id, StringComparison.Ordinal))
            {
                outcome = (UserSuspendOutcome.SelfActionBlocked, targetName);
                return;
            }

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var result = await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                outcome = (UserSuspendOutcome.ValidationFailed, targetName);
                return;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            outcome = (UserSuspendOutcome.Succeeded, targetName);
        });

        return outcome;
    }

    public async Task<(UserDeleteOutcome Status, string TargetName)> DeleteUserAsync(
        string userId, string? actingUserId, CancellationToken cancellationToken = default)
    {
        var outcome = (Status: UserDeleteOutcome.UserNotFound, TargetName: userId);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = (UserDeleteOutcome.UserNotFound, userId);

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return;
            }

            var targetName = user.UserName ?? user.Id;

            if (!string.IsNullOrWhiteSpace(actingUserId) && string.Equals(actingUserId, user.Id, StringComparison.Ordinal))
            {
                outcome = (UserDeleteOutcome.SelfActionBlocked, targetName);
                return;
            }

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                outcome = (UserDeleteOutcome.ValidationFailed, targetName);
                return;
            }

            // Note: Since DeleteAsync doesn't always automatically persist everything depending on how UserManager is configured, 
            // ensure DB is saved.
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            outcome = (UserDeleteOutcome.Succeeded, targetName);
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
