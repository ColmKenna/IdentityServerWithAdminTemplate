using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Threading.Tasks;
using IdentityServerProject.Data;
using IdentityServerProject.Data.Adapters;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Users;

/// <summary>
/// Focused adapter tests for <see cref="EfIdentityUserAdministrationStore"/> covering paths that
/// need a mocked <see cref="UserManager{TUser}"/> rather than end-to-end DI resolution - namely,
/// how a mid-sequence Identity failure during unlock is reported without partially applying the
/// remaining step. The rest of the port's behavior is exercised indirectly through
/// <see cref="UserListServiceTests"/>, <see cref="UserCreateServiceTests"/>, and
/// <see cref="UserDetailsServiceTests"/> via the real (SQLite in-memory) DI container.
/// </summary>
public class EfIdentityUserAdministrationStoreTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public EfIdentityUserAdministrationStoreTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static ApplicationUser MakeUser(string tag, string suffix) => new()
    {
        Id = $"{tag}-user-{suffix}",
        UserName = $"{tag}-username-{suffix}",
        Email = $"{tag}-email-{suffix}@sales.local",
        FullName = $"{tag} User {suffix}",
        LockoutEnabled = true,
    };

    [Fact]
    public async Task UnlockUserAsync_SetLockoutFailure_ReturnsIdentityErrorsWithoutResettingAccessFailures()
    {
        var user = MakeUser(Guid.NewGuid().ToString("N"), "lockout-failure");
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.FindByIdAsync(user.Id)).ReturnsAsync(user);
        userManager
            .Setup(manager => manager.SetLockoutEndDateAsync(user, (DateTimeOffset?)null))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Lockout state update failed." }));

        await _factory.RunInScopeAsync(async sp =>
        {
            var store = new EfIdentityUserAdministrationStore(
                sp.GetRequiredService<ApplicationDbContext>(),
                userManager.Object,
                sp.GetRequiredService<RoleManager<IdentityRole>>());

            var outcome = await store.UnlockUserAsync(user.Id);

            Assert.Equal(UserUnlockStatus.Failed, outcome.Result.Status);
            Assert.Equal("Lockout state update failed.", Assert.Single(outcome.Result.Errors));
            Assert.Equal(user.UserName, outcome.TargetName);
            userManager.Verify(manager => manager.ResetAccessFailedCountAsync(It.IsAny<ApplicationUser>()), Times.Never);
        });
    }

    [Fact]
    public async Task UnlockUserAsync_ResetAccessFailuresFailure_ReturnsIdentityErrors()
    {
        var user = MakeUser(Guid.NewGuid().ToString("N"), "reset-failure");
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.FindByIdAsync(user.Id)).ReturnsAsync(user);
        userManager
            .Setup(manager => manager.SetLockoutEndDateAsync(user, (DateTimeOffset?)null))
            .ReturnsAsync(IdentityResult.Success);
        userManager
            .Setup(manager => manager.ResetAccessFailedCountAsync(user))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Access-failure reset failed." }));

        await _factory.RunInScopeAsync(async sp =>
        {
            var store = new EfIdentityUserAdministrationStore(
                sp.GetRequiredService<ApplicationDbContext>(),
                userManager.Object,
                sp.GetRequiredService<RoleManager<IdentityRole>>());

            var outcome = await store.UnlockUserAsync(user.Id);

            Assert.Equal(UserUnlockStatus.Failed, outcome.Result.Status);
            Assert.Equal("Access-failure reset failed.", Assert.Single(outcome.Result.Errors));
        });
    }

    [Fact]
    public async Task UnlockUserAsync_UserNotFound_ReturnsNotFoundWithRawIdAsTargetName()
    {
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.FindByIdAsync("missing-user")).ReturnsAsync((ApplicationUser?)null);

        await _factory.RunInScopeAsync(async sp =>
        {
            var store = new EfIdentityUserAdministrationStore(
                sp.GetRequiredService<ApplicationDbContext>(),
                userManager.Object,
                sp.GetRequiredService<RoleManager<IdentityRole>>());

            var outcome = await store.UnlockUserAsync("missing-user");

            Assert.Equal(UserUnlockStatus.NotFound, outcome.Result.Status);
            Assert.Equal("missing-user", outcome.TargetName);
        });
    }

    private static Mock<UserManager<ApplicationUser>> CreateUserManager() => new(
        Mock.Of<IUserStore<ApplicationUser>>(),
        null!,
        null!,
        null!,
        null!,
        null!,
        null!,
        null!,
        null!);
}
