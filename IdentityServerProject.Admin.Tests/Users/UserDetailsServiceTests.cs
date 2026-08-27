using System.Security.Claims;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Services.Users;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UserClaim = IdentityServerProject.Services.Users.UserClaim;

namespace IdentityServerProject.Admin.Tests.Users;

/// <summary>
///     Exercises <see cref="UserDetailsService" /> against a real (SQLite in-memory) DI container.
///     Every test uses a unique tag for role/user names so assertions are unaffected by data left
///     behind by other tests sharing the same connection (AdminWebFactory is a class fixture).
/// </summary>
public class UserDetailsServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public UserDetailsServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static async Task<ApplicationUser> CreateUserAsync(UserManager<ApplicationUser> userManager, string tag,
        string suffix)
    {
        var user = new ApplicationUser
        {
            UserName = $"{tag}-user-{suffix}",
            Email = $"{tag}-{suffix}@sales.local",
            FullName = $"{tag} User {suffix}"
        };
        IdentityResult result = await userManager.CreateAsync(user, "Password123!");
        Assert.True(result.Succeeded);
        return user;
    }

    [Fact]
    public async Task GetUserDetailsAsync_NonExistentUser_ReturnsNull()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            UserDetailsModel? result =
                await service.GetUserDetailsAsync(
                    new UserActionContext(UserId.Create("non-existent-user-id-xyz"), null));
            Assert.Null(result);
        });
    }

    [Fact]
    public async Task GetUserDetailsAsync_ExistingUser_ReturnsRolesClaimsAndSelfFlag()
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            RoleManager<IdentityRole> roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
            string roleName = $"{tag}-role";
            await roleManager.CreateAsync(new IdentityRole(roleName));

            ApplicationUser user = await CreateUserAsync(userManager, tag, "alpha");
            await userManager.AddToRoleAsync(user, roleName);
            await userManager.AddClaimAsync(user, new Claim("dept", "Engineering"));

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();

            UserDetailsModel? result =
                await service.GetUserDetailsAsync(new UserActionContext(UserId.Create(user.Id),
                    UserId.Create(user.Id)));
            Assert.NotNull(result);
            Assert.Contains(roleName, result!.AssignedRoles);
            Assert.Contains(roleName, result.AllRoles);
            Assert.Contains(result.Claims, c => c.Type == "dept" && c.Value == "Engineering");
            Assert.True(result.IsCurrentUser);

            UserDetailsModel? resultAsOther =
                await service.GetUserDetailsAsync(new UserActionContext(UserId.Create(user.Id),
                    UserId.Create("someone-else-id")));
            Assert.False(resultAsOther!.IsCurrentUser);
        });
    }

    [Fact]
    public async Task AddRoleAsync_ExistingRole_AssignsRole()
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            RoleManager<IdentityRole> roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
            string roleName = $"{tag}-role";
            await roleManager.CreateAsync(new IdentityRole(roleName));
            ApplicationUser user = await CreateUserAsync(userManager, tag, "addrole");

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            RoleChangeResult result = await service.AddRoleAsync(UserId.Create(user.Id), roleName);

            Assert.True(result.Success);
            Assert.True(await userManager.IsInRoleAsync(user, roleName));
        });
    }

    [Fact]
    public async Task RemoveRoleAsync_NotSoleHolder_Succeeds()
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            RoleManager<IdentityRole> roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
            string roleName = $"{tag}-role";
            await roleManager.CreateAsync(new IdentityRole(roleName));

            ApplicationUser userA = await CreateUserAsync(userManager, tag, "a");
            ApplicationUser userB = await CreateUserAsync(userManager, tag, "b");
            await userManager.AddToRoleAsync(userA, roleName);
            await userManager.AddToRoleAsync(userB, roleName);

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            RoleChangeResult result = await service.RemoveRoleAsync(UserId.Create(userA.Id), roleName);

            Assert.True(result.Success);
            Assert.False(await userManager.IsInRoleAsync(userA, roleName));
            Assert.True(await userManager.IsInRoleAsync(userB, roleName));
        });
    }

    [Fact]
    public async Task RemoveRoleAsync_SoleHolderOfOrdinaryRole_Succeeds()
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            RoleManager<IdentityRole> roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
            string roleName = $"{tag}-role";
            await roleManager.CreateAsync(new IdentityRole(roleName));

            ApplicationUser soleHolder = await CreateUserAsync(userManager, tag, "sole");
            await userManager.AddToRoleAsync(soleHolder, roleName);

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            RoleChangeResult result = await service.RemoveRoleAsync(UserId.Create(soleHolder.Id), roleName);

            Assert.True(result.Success);
            Assert.False(await userManager.IsInRoleAsync(soleHolder, roleName));
        });
    }

    [Fact]
    public async Task AddClaimAsync_Then_RemoveClaimAsync_RoundTrips()
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "claims");

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();

            ClaimChangeResult addResult =
                await service.AddClaimAsync(UserId.Create(user.Id), new UserClaim("dept", "Sales"));
            Assert.True(addResult.Success);

            UserDetailsModel? afterAdd =
                await service.GetUserDetailsAsync(new UserActionContext(UserId.Create(user.Id), null));
            Assert.Contains(afterAdd!.Claims, c => c.Type == "dept" && c.Value == "Sales");

            ClaimChangeResult removeResult =
                await service.RemoveClaimAsync(UserId.Create(user.Id), new UserClaim("dept", "Sales"));
            Assert.True(removeResult.Success);

            UserDetailsModel? afterRemove =
                await service.GetUserDetailsAsync(new UserActionContext(UserId.Create(user.Id), null));
            Assert.DoesNotContain(afterRemove!.Claims, c => c.Type == "dept" && c.Value == "Sales");
        });
    }

    [Theory]
    // Short OIDC/JWT forms and the WS-* URI forms Identity authorization reads by default, plus
    // the casing and whitespace variants that ClaimsIdentity would still honour.
    [InlineData("role")]
    [InlineData("roles")]
    [InlineData("ROLE")]
    [InlineData(" role ")]
    [InlineData("sub")]
    [InlineData("amr")]
    [InlineData("idp")]
    [InlineData("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")]
    [InlineData("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")]
    [InlineData("AspNet.Identity.SecurityStamp")]
    public async Task AddClaimAsync_ReservedClaimType_IsRejectedAndNothingIsStored(string claimType)
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "reserved");

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            ClaimChangeResult result =
                await service.AddClaimAsync(UserId.Create(user.Id), new UserClaim(claimType, Config.SysAdminRole));

            Assert.False(result.Success);
            Assert.Contains("reserved", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await userManager.GetClaimsAsync(user));
        });
    }

    [Fact]
    public void ConfiguredRoleClaimType_IsTheShortFormBecauseAddAspNetIdentityOverridesIt()
    {
        // Duende's AddAspNetIdentity<TUser>() rewrites IdentityOptions.ClaimsIdentity to the JWT
        // short forms, so the type that actually satisfies RequireRole here is "role" - NOT
        // ClaimTypes.Role, which is what an untested deny-list would most likely have blocked.
        // The deny-list covers both, and derives the live value from options rather than assuming.
        IdentityOptions identityOptions = _factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.Equal("role", identityOptions.ClaimsIdentity.RoleClaimType);
        Assert.Equal("sub", identityOptions.ClaimsIdentity.UserIdClaimType);
    }

    [Fact]
    public async Task AddClaimAsync_RoleClaim_CannotGrantSysAdminOnThePrincipal()
    {
        // The reason the guard exists: UserClaimsPrincipalFactory copies stored user claims onto
        // the principal verbatim, so a role-typed user claim satisfies RequireRole(SysAdmin)
        // without any AspNetUserRoles row - invisible to the Roles tab, to GetUsersInRoleAsync,
        // and to the Last-Admin Guard that counts role holders.
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            string roleClaimType =
                sp.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity.RoleClaimType;
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "escalate");

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            ClaimChangeResult result = await service.AddClaimAsync(UserId.Create(user.Id),
                new UserClaim(roleClaimType, Config.SysAdminRole));

            Assert.False(result.Success);

            IUserClaimsPrincipalFactory<ApplicationUser> principalFactory =
                sp.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
            ClaimsPrincipal principal = await principalFactory.CreateAsync(user);

            Assert.False(principal.IsInRole(Config.SysAdminRole));
            Assert.DoesNotContain(await userManager.GetRolesAsync(user), r => r == Config.SysAdminRole);
        });
    }

    [Fact]
    public async Task RoleClaimWrittenOutsideTheEditor_WouldGrantSysAdmin()
    {
        // Characterises the framework behaviour the guard defends against, using the role claim
        // type this deployment is actually configured with. If this ever stops holding, the
        // deny-list is protecting against something that no longer exists and should be revisited
        // - it is not asserting desirable behaviour.
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            string roleClaimType =
                sp.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity.RoleClaimType;
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "characterise");

            await userManager.AddClaimAsync(user, new Claim(roleClaimType, Config.SysAdminRole));

            IUserClaimsPrincipalFactory<ApplicationUser> principalFactory =
                sp.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
            ClaimsPrincipal principal = await principalFactory.CreateAsync(user);

            Assert.True(principal.IsInRole(Config.SysAdminRole));

            // ...and it is invisible everywhere a role grant is meant to be visible.
            Assert.Empty(await userManager.GetRolesAsync(user));
            Assert.DoesNotContain(await userManager.GetUsersInRoleAsync(Config.SysAdminRole), u => u.Id == user.Id);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddClaimAsync_BlankClaimType_IsRejectedWithoutThrowing(string claimType)
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "blank");

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            ClaimChangeResult result =
                await service.AddClaimAsync(UserId.Create(user.Id), new UserClaim(claimType, "anything"));

            Assert.False(result.Success);
            Assert.Contains("required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await userManager.GetClaimsAsync(user));
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddClaimAsync_BlankClaimValue_IsRejectedWithoutMutation(string claimValue)
    {
        string tag = Guid.NewGuid().ToString("N");
        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "blank-value");
            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();

            ClaimChangeResult result =
                await service.AddClaimAsync(UserId.Create(user.Id), new UserClaim("dept", claimValue));

            Assert.False(result.Success);
            Assert.Contains("required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await userManager.GetClaimsAsync(user));
        });
    }

    [Fact]
    public async Task AddClaimAsync_MaximumLengths_AreAccepted()
    {
        string tag = Guid.NewGuid().ToString("N");
        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "max-claim");
            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            string type = new('t', ValidationConstants.MaxClaimTypeLength);
            string value = new('v', ValidationConstants.MaxClaimValueLength);

            ClaimChangeResult result = await service.AddClaimAsync(UserId.Create(user.Id), new UserClaim(type, value));

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains(await userManager.GetClaimsAsync(user),
                claim => claim.Type == type && claim.Value == value);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddClaimAsync_OverMaximumLength_IsRejected(bool overlongType)
    {
        string tag = Guid.NewGuid().ToString("N");
        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "long-claim");
            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            string type = overlongType ? new string('t', ValidationConstants.MaxClaimTypeLength + 1) : "dept";
            string value = overlongType ? "Sales" : new string('v', ValidationConstants.MaxClaimValueLength + 1);

            ClaimChangeResult result = await service.AddClaimAsync(UserId.Create(user.Id), new UserClaim(type, value));

            Assert.False(result.Success);
            Assert.Contains("cannot exceed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await userManager.GetClaimsAsync(user));
        });
    }

    [Fact]
    public async Task AddClaimAsync_ExactDuplicate_IsRejectedAndStoredOnce()
    {
        string tag = Guid.NewGuid().ToString("N");
        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "duplicate-claim");
            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();

            Assert.True((await service.AddClaimAsync(UserId.Create(user.Id), new UserClaim("dept", "Sales"))).Success);
            ClaimChangeResult duplicate =
                await service.AddClaimAsync(UserId.Create(user.Id), new UserClaim("dept", "Sales"));

            Assert.False(duplicate.Success);
            Assert.Contains("already", duplicate.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Single(await userManager.GetClaimsAsync(user),
                claim => claim.Type == "dept" && claim.Value == "Sales");
        });
    }

    [Theory]
    // Ordinary profile and application claims must keep working against the *real* container
    // config, not just IdentityOptions defaults. "name" matters most: Duende sets
    // UserNameClaimType to it and SeedData writes it onto every user, so a deny-list derived
    // naively from IdentityOptions would break seeding and every existing account.
    [InlineData("name")]
    [InlineData("email")]
    [InlineData("dept")]
    [InlineData("given_name")]
    public async Task AddClaimAsync_OrdinaryClaimType_IsAcceptedUnderTheRealConfiguration(string claimType)
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, $"ordinary-{claimType}");

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            ClaimChangeResult result =
                await service.AddClaimAsync(UserId.Create(user.Id), new UserClaim(claimType, "some-value"));

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains(await userManager.GetClaimsAsync(user),
                c => c.Type == claimType && c.Value == "some-value");
        });
    }

    [Fact]
    public async Task AddClaimAsync_TrimsTheClaimTypeBeforeStoring()
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "trim");

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            Assert.True((await service.AddClaimAsync(UserId.Create(user.Id), new UserClaim("  dept  ", "Sales")))
                .Success);

            IList<Claim> claims = await userManager.GetClaimsAsync(user);
            Assert.Contains(claims, c => c.Type == "dept");
        });
    }

    [Fact]
    public async Task RemoveClaimAsync_ReservedClaim_IsStillAllowed()
    {
        // Removal only ever de-escalates, and reserved claims written before the guard existed
        // need a cleanup path through the same UI that surfaces them.
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "cleanup");
            await userManager.AddClaimAsync(user, new Claim(ClaimTypes.Role, Config.SysAdminRole));

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            ClaimChangeResult result = await service.RemoveClaimAsync(UserId.Create(user.Id),
                new UserClaim(ClaimTypes.Role, Config.SysAdminRole));

            Assert.True(result.Success);
            Assert.Empty(await userManager.GetClaimsAsync(user));
        });
    }

    [Fact]
    public async Task GetUserDetailsAsync_FlagsReservedClaimsForRemediation()
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "flagged");
            await userManager.AddClaimAsync(user, new Claim(ClaimTypes.Role, Config.SysAdminRole));
            await userManager.AddClaimAsync(user, new Claim("dept", "Engineering"));

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            UserDetailsModel? result =
                await service.GetUserDetailsAsync(new UserActionContext(UserId.Create(user.Id), null));

            Assert.True(result!.Claims.Single(c => c.Type == ClaimTypes.Role).IsReserved);
            Assert.False(result.Claims.Single(c => c.Type == "dept").IsReserved);
        });
    }

    [Fact]
    public async Task RevokeUserAccessAsync_OtherUser_RevokesGrantsAndReturnsCount()
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "sessions");

            PersistedGrantDbContext grantDb = sp.GetRequiredService<PersistedGrantDbContext>();
            grantDb.PersistedGrants.Add(new PersistedGrant
            {
                Key = $"{tag}-grant-1",
                Type = "user_consent",
                ClientId = $"{tag}-client",
                SubjectId = user.Id,
                CreationTime = DateTime.UtcNow,
                Expiration = DateTime.UtcNow.AddDays(1),
                Data = "{}"
            });
            await grantDb.SaveChangesAsync();

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            UserAccessRevokeResult result =
                await service.RevokeUserAccessAsync(new UserActionContext(UserId.Create(user.Id),
                    UserId.Create("some-other-admin-id")));

            Assert.True(result.Success);
            Assert.Equal(1, result.RevokedGrantCount);
            Assert.Empty(grantDb.PersistedGrants.Where(g => g.SubjectId == user.Id));
        });
    }

    [Fact]
    public async Task RevokeUserAccessAsync_Self_IsBlockedByViewingSelfGuard()
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "self");

            PersistedGrantDbContext grantDb = sp.GetRequiredService<PersistedGrantDbContext>();
            grantDb.PersistedGrants.Add(new PersistedGrant
            {
                Key = $"{tag}-self-grant",
                Type = "user_consent",
                ClientId = $"{tag}-client",
                SubjectId = user.Id,
                CreationTime = DateTime.UtcNow,
                Expiration = DateTime.UtcNow.AddDays(1),
                Data = "{}"
            });
            await grantDb.SaveChangesAsync();

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            UserAccessRevokeResult result =
                await service.RevokeUserAccessAsync(new UserActionContext(UserId.Create(user.Id),
                    UserId.Create(user.Id)));

            Assert.False(result.Success);
            Assert.Contains("own", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Single(grantDb.PersistedGrants.Where(g => g.SubjectId == user.Id));
        });
    }

    [Fact]
    public async Task UnlockUserAsync_LockedUser_UnlocksSuccessfully()
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "locked");
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(2));

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            UserUnlockResult result = await service.UnlockUserAsync(UserId.Create(user.Id));

            Assert.Equal(UserUnlockStatus.Succeeded, result.Status);

            ApplicationUser? refreshedUser = await userManager.FindByIdAsync(user.Id);
            Assert.False(refreshedUser!.LockoutEnd.HasValue && refreshedUser.LockoutEnd.Value > DateTimeOffset.UtcNow);
        });
    }

    [Fact]
    public async Task UnlockUserAsync_NonExistentUser_ReturnsNotFound()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            UserUnlockResult result = await service.UnlockUserAsync(UserId.Create("non-existent-user-id"));

            Assert.Equal(UserUnlockStatus.NotFound, result.Status);
        });
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidPassword_ReplacesTheExistingPassword()
    {
        string tag = Guid.NewGuid().ToString("N");
        string userId = string.Empty;

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "reset-password");
            userId = user.Id;
            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();

            PasswordResetResult result = await service.ResetPasswordAsync(UserId.Create(user.Id), "NewPassword123!");

            Assert.True(result.Success, result.ErrorMessage);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser? user = await userManager.FindByIdAsync(userId);

            Assert.NotNull(user);
            Assert.False(await userManager.CheckPasswordAsync(user!, "Password123!"));
            Assert.True(await userManager.CheckPasswordAsync(user, "NewPassword123!"));
        });
    }

    [Fact]
    public async Task ResetPasswordAsync_BlankPassword_IsRejectedWithoutChangingTheExistingPassword()
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "blank-password");
            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();

            PasswordResetResult result = await service.ResetPasswordAsync(UserId.Create(user.Id), "   ");

            Assert.False(result.Success);
            Assert.Equal("Password is required.", result.ErrorMessage);
            Assert.True(await userManager.CheckPasswordAsync(user, "Password123!"));
        });
    }

    [Fact]
    public async Task SuspendUserAsync_ExistingUser_SetsAPersistentLockout()
    {
        string tag = Guid.NewGuid().ToString("N");
        string userId = string.Empty;

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "suspended");
            userId = user.Id;

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            UserSuspendResult result =
                await service.SuspendUserAsync(new UserActionContext(UserId.Create(user.Id),
                    UserId.Create("another-administrator")));

            Assert.True(result.Success, result.ErrorMessage);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser? user = await userManager.FindByIdAsync(userId);

            Assert.NotNull(user);
            Assert.True(user!.LockoutEnd > DateTimeOffset.UtcNow);
        });
    }

    [Fact]
    public async Task SuspendUserAsync_MissingUser_ReturnsTheCurrentNotFoundResult()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            UserSuspendResult result = await service.SuspendUserAsync(
                new UserActionContext(UserId.Create($"missing-{Guid.NewGuid():N}"),
                    UserId.Create("another-administrator")));

            Assert.False(result.Success);
            Assert.Equal("User not found.", result.ErrorMessage);
        });
    }

    [Fact]
    public async Task DeleteUserAsync_ExistingUser_RemovesTheUserAndTheirPersistedGrants()
    {
        string tag = Guid.NewGuid().ToString("N");
        string userId = string.Empty;

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "deleted");
            userId = user.Id;

            PersistedGrantDbContext grantDb = sp.GetRequiredService<PersistedGrantDbContext>();
            grantDb.PersistedGrants.Add(new PersistedGrant
            {
                Key = $"{tag}-delete-grant",
                Type = "user_consent",
                ClientId = $"{tag}-client",
                SubjectId = user.Id,
                CreationTime = DateTime.UtcNow,
                Expiration = DateTime.UtcNow.AddDays(1),
                Data = "{}"
            });
            await grantDb.SaveChangesAsync();

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            UserDeleteResult result =
                await service.DeleteUserAsync(new UserActionContext(UserId.Create(user.Id),
                    UserId.Create("another-administrator")));

            Assert.True(result.Success, result.ErrorMessage);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            PersistedGrantDbContext grantDb = sp.GetRequiredService<PersistedGrantDbContext>();

            Assert.Null(await userManager.FindByIdAsync(userId));
            Assert.Empty(grantDb.PersistedGrants.Where(grant => grant.SubjectId == userId));
        });
    }

    [Fact]
    public async Task DeleteUserAsync_GrantCleanupFailure_StillDeletesTheUserAndReturnsSuccess()
    {
        string tag = Guid.NewGuid().ToString("N");
        string userId = string.Empty;

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await CreateUserAsync(userManager, tag, "delete-cleanup-failure");
            userId = user.Id;

            PersistedGrantDbContext grantDb = sp.GetRequiredService<PersistedGrantDbContext>();
            grantDb.PersistedGrants.Add(new PersistedGrant
            {
                Key = $"{tag}-cleanup-failure-grant",
                Type = "user_consent",
                ClientId = $"{tag}-client",
                SubjectId = user.Id,
                CreationTime = DateTime.UtcNow,
                Expiration = DateTime.UtcNow.AddDays(1),
                Data = "{}"
            });
            await grantDb.SaveChangesAsync();

            IUserDetailsService service = sp.GetRequiredService<IUserDetailsService>();
            await grantDb.DisposeAsync();

            UserDeleteResult result =
                await service.DeleteUserAsync(new UserActionContext(UserId.Create(user.Id),
                    UserId.Create("another-administrator")));

            Assert.True(result.Success, result.ErrorMessage);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            PersistedGrantDbContext grantDb = sp.GetRequiredService<PersistedGrantDbContext>();

            Assert.Null(await userManager.FindByIdAsync(userId));
            Assert.Single(grantDb.PersistedGrants.Where(grant => grant.SubjectId == userId));
        });
    }
}