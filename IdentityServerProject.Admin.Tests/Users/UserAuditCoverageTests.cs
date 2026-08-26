using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;
using IdentityServerProject;
using IdentityServerProject.Data;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Users;

/// <summary>
/// Verifies TASK-04 audit coverage for the Users services: <see cref="UserCreateService"/>
/// and <see cref="UserListService.UnlockUserAsync"/> (neither previously injected
/// <see cref="IAuditWriter"/> at all), plus the previously success-only
/// <see cref="UserDetailsService"/> denial paths.
/// </summary>
public class UserAuditCoverageTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public UserAuditCoverageTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private async Task<ApplicationUser> CreateUserAsync(string tag, string suffix)
    {
        ApplicationUser? user = null;
        await _factory.RunInScopeAsync(async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            user = new ApplicationUser
            {
                UserName = $"{tag}-user-{suffix}",
                Email = $"{tag}-{suffix}@sales.local",
                FullName = $"{tag} User {suffix}"
            };
            var result = await userManager.CreateAsync(user, "Password123!");
            Assert.True(result.Succeeded);
        });
        return user!;
    }

    private async Task<AuditLogEntry> GetSingleAuditEntryAsync(string category, string action, string targetId)
    {
        AuditLogEntry? found = null;
        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            found = db.AuditLogEntries.Single(e => e.Category == category && e.Action == action && e.TargetId == targetId);
            await Task.CompletedTask;
        });
        return found!;
    }

    private async Task<AuditLogEntry> GetSingleAuditEntryByTargetNameAsync(string category, string action, string targetName)
    {
        AuditLogEntry? found = null;
        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            found = db.AuditLogEntries.Single(e => e.Category == category && e.Action == action && e.TargetName == targetName);
            await Task.CompletedTask;
        });
        return found!;
    }

    [Fact]
    public async Task CreateUserAsync_ValidInput_WritesSucceededAuditEvent()
    {
        var tag = $"user-audit-create-{Guid.NewGuid():N}";
        var input = new UserCreateInputModel
        {
            UserName = $"{tag}-username",
            Email = $"{tag}-email@sales.local",
            FullName = $"{tag} Full Name",
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        };

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserCreateService>();
            var result = await service.CreateUserAsync(input);
            Assert.True(result.Success);
        });

        var entry = await GetSingleAuditEntryByTargetNameAsync(AuditCategory.User, AuditAction.Create, input.UserName);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
        Assert.Equal(AuditReasonCode.Succeeded, entry.ReasonCode);
    }

    [Fact]
    public async Task CreateUserAsync_DuplicateEmail_WritesDeniedAuditEvent()
    {
        var tag = $"user-audit-dup-{Guid.NewGuid():N}";
        var email = $"{tag}-email@sales.local";

        var firstInput = new UserCreateInputModel
        {
            UserName = $"{tag}-first",
            Email = email,
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        };
        var duplicateInput = new UserCreateInputModel
        {
            UserName = $"{tag}-second",
            Email = email,
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        };

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserCreateService>();
            var first = await service.CreateUserAsync(firstInput);
            Assert.True(first.Success);

            var duplicate = await service.CreateUserAsync(duplicateInput);
            Assert.False(duplicate.Success);
        });

        var entry = await GetSingleAuditEntryAsync(AuditCategory.User, AuditAction.Create, duplicateInput.UserName);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
    }

    [Fact]
    public async Task UnlockUserAsync_UserNotFound_WritesDeniedAuditEvent()
    {
        var missingId = $"user-audit-unlock-missing-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserListService>();
            var result = await service.UnlockUserAsync(UserId.Create(missingId));
            Assert.Equal(UserUnlockStatus.NotFound, result.Status);
        });

        var entry = await GetSingleAuditEntryAsync(AuditCategory.User, AuditAction.Unlock, missingId);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task UnlockUserAsync_ExistingUser_WritesSucceededAuditEvent()
    {
        var tag = $"user-audit-unlock-{Guid.NewGuid():N}";
        var user = await CreateUserAsync(tag, "locked");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserListService>();
            var result = await service.UnlockUserAsync(UserId.Create(user.Id));
            Assert.Equal(UserUnlockStatus.Succeeded, result.Status);
        });

        var entry = await GetSingleAuditEntryAsync(AuditCategory.User, AuditAction.Unlock, user.Id);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task RemoveRoleAsync_SoleHolderOfOrdinaryRole_WritesSucceededAuditEvent()
    {
        // Uses a custom (non-SysAdmin) role: the seeded default SysAdmin account (created at
        // every app startup by SeedSysAdminAsync) would otherwise be a second SysAdmin holder
        // and defeat a same-role "sole holder" scenario.
        var tag = $"user-audit-lastrole-{Guid.NewGuid():N}";
        var roleName = $"{tag}-role";
        string soleHolderId = string.Empty;

        // Role creation, user creation, and AddToRoleAsync must share one DbContext instance:
        // UserManager.AddToRoleAsync ultimately Attach()es the passed-in user, which throws if
        // that same key is already tracked by a different ApplicationUser instance in a
        // different scope's context.
        await _factory.RunInScopeAsync(async sp =>
        {
            var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
            await roleManager.CreateAsync(new IdentityRole(roleName));

            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var soleHolder = new ApplicationUser
            {
                UserName = $"{tag}-user-sole-holder",
                Email = $"{tag}-sole-holder@sales.local",
                FullName = $"{tag} Sole Holder"
            };
            var createResult = await userManager.CreateAsync(soleHolder, "Password123!");
            Assert.True(createResult.Succeeded);
            soleHolderId = soleHolder.Id;

            await userManager.AddToRoleAsync(soleHolder, roleName);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserDetailsService>();
            var result = await service.RemoveRoleAsync(UserId.Create(soleHolderId), roleName);
            Assert.True(result.Success);
        });

        var entry = await GetSingleAuditEntryAsync(AuditCategory.User, AuditAction.RemoveRole, soleHolderId);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
        Assert.Equal(AuditReasonCode.Succeeded, entry.ReasonCode);
    }

    [Fact]
    public async Task RevokeUserAccessAsync_SelfRevoke_WritesDeniedAuditEventWithSelfActionReason()
    {
        var tag = $"user-audit-selfrevoke-{Guid.NewGuid():N}";
        var user = await CreateUserAsync(tag, "self");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserDetailsService>();
            var result = await service.RevokeUserAccessAsync(UserId.Create(user.Id), UserId.Create(user.Id));
            Assert.False(result.Success);
        });

        var entry = await GetSingleAuditEntryAsync(AuditCategory.User, AuditAction.RevokeUserAccess, user.Id);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.SelfAction, entry.ReasonCode);
    }

    [Fact]
    public async Task RevokeUserAccessAsync_ByAnotherAdmin_WritesSucceededAuditEvent()
    {
        var tag = $"user-audit-revoke-{Guid.NewGuid():N}";
        var target = await CreateUserAsync(tag, "target");
        var admin = await CreateUserAsync(tag, "admin");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserDetailsService>();
            var result = await service.RevokeUserAccessAsync(UserId.Create(target.Id), UserId.Create(admin.Id));
            Assert.True(result.Success);
        });

        var entry = await GetSingleAuditEntryAsync(AuditCategory.User, AuditAction.RevokeUserAccess, target.Id);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task UnlockUserAsync_UnexpectedPersistenceFailure_WritesFailedAuditEventAndRethrows()
    {
        var targetId = $"user-audit-failure-{Guid.NewGuid():N}";

        await Assert.ThrowsAnyAsync<Exception>(() => _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserListService>();
            await sp.GetRequiredService<ApplicationDbContext>().DisposeAsync();
            await service.UnlockUserAsync(UserId.Create(targetId));
        }));

        var entry = await GetSingleAuditEntryAsync(AuditCategory.User, AuditAction.Unlock, targetId);
        Assert.Equal(AuditOutcome.Failed, entry.Outcome);
        Assert.Equal(AuditReasonCode.PersistenceFailure, entry.ReasonCode);
    }

    [Fact]
    public async Task SuspendUserAsync_ByAnotherAdmin_WritesSucceededAuditEvent()
    {
        var tag = $"user-audit-suspend-{Guid.NewGuid():N}";
        var target = await CreateUserAsync(tag, "target");
        var admin = await CreateUserAsync(tag, "admin");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserDetailsService>();
            var result = await service.SuspendUserAsync(UserId.Create(target.Id), UserId.Create(admin.Id));
            Assert.True(result.Success);
        });

        var entry = await GetSingleAuditEntryAsync(AuditCategory.User, AuditAction.SuspendUser, target.Id);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
        Assert.Equal(AuditReasonCode.Succeeded, entry.ReasonCode);
    }

    [Fact]
    public async Task SuspendUserAsync_SelfSuspend_WritesDeniedAuditEventWithSelfActionReason()
    {
        var tag = $"user-audit-selfsuspend-{Guid.NewGuid():N}";
        var user = await CreateUserAsync(tag, "self");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserDetailsService>();
            var result = await service.SuspendUserAsync(UserId.Create(user.Id), UserId.Create(user.Id));
            Assert.False(result.Success);
        });

        var entry = await GetSingleAuditEntryAsync(AuditCategory.User, AuditAction.SuspendUser, user.Id);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.SelfAction, entry.ReasonCode);
    }

    [Fact]
    public async Task DeleteUserAsync_ByAnotherAdmin_WritesSucceededAuditEvent()
    {
        var tag = $"user-audit-del-{Guid.NewGuid():N}";
        var target = await CreateUserAsync(tag, "target");
        var admin = await CreateUserAsync(tag, "admin");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserDetailsService>();
            var result = await service.DeleteUserAsync(UserId.Create(target.Id), UserId.Create(admin.Id));
            Assert.True(result.Success);
        });

        var entry = await GetSingleAuditEntryAsync(AuditCategory.User, AuditAction.DeleteUser, target.Id);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
        Assert.Equal(AuditReasonCode.Succeeded, entry.ReasonCode);
    }

    [Fact]
    public async Task DeleteUserAsync_SelfDelete_WritesDeniedAuditEventWithSelfActionReason()
    {
        var tag = $"user-audit-selfdel-{Guid.NewGuid():N}";
        var user = await CreateUserAsync(tag, "self");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserDetailsService>();
            var result = await service.DeleteUserAsync(UserId.Create(user.Id), UserId.Create(user.Id));
            Assert.False(result.Success);
        });

        var entry = await GetSingleAuditEntryAsync(AuditCategory.User, AuditAction.DeleteUser, user.Id);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.SelfAction, entry.ReasonCode);
    }
}
