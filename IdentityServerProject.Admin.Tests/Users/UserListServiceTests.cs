using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Threading.Tasks;
using IdentityServerProject.Data;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Users;

/// <summary>
/// Exercises <see cref="UserListService"/> against a real (SQLite in-memory)
/// <see cref="ApplicationDbContext"/> resolved from the shared <see cref="AdminWebFactory"/> DI container.
/// Every test seeds users with a unique tag embedded in the user properties so
/// assertions are unaffected by data left behind by other tests sharing the same connection.
/// </summary>
public class UserListServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public UserListServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static ApplicationUser MakeUser(string tag, string suffix, bool lockedOut = false)
    {
        return new ApplicationUser
        {
            Id = $"{tag}-user-{suffix}",
            UserName = $"{tag}-username-{suffix}",
            Email = $"{tag}-email-{suffix}@sales.local",
            FullName = $"{tag} User {suffix}",
            LockoutEnabled = true,
            LockoutEnd = lockedOut ? DateTimeOffset.UtcNow.AddDays(7) : null,
        };
    }

    private async Task SeedAsync(params ApplicationUser[] users)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            foreach (var user in users)
            {
                await userManager.CreateAsync(user, "Password123!");
            }
        });
    }

    [Fact]
    public async Task GetUsersAsync_FilterMatchesUserName_ReturnsOnlyMatchingUser()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeUser(tag, "alpha"), MakeUser(tag, "beta"));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserListService>();

            var result = await service.GetUsersAsync($"{tag}-username-alpha", pageNumber: 1, pageSize: 10);

            var item = Assert.Single(result.Items);
            Assert.Equal($"{tag}-user-alpha", item.Id);
        });
    }

    [Fact]
    public async Task GetUsersAsync_FilterMatchesEmail_ReturnsOnlyMatchingUser()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeUser(tag, "gamma"), MakeUser(tag, "delta"));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserListService>();

            var result = await service.GetUsersAsync($"{tag}-email-delta", pageNumber: 1, pageSize: 10);

            var item = Assert.Single(result.Items);
            Assert.Equal($"{tag}-user-delta", item.Id);
        });
    }

    [Fact]
    public async Task GetUsersAsync_Pagination_ReturnsCorrectPageAndTotalCount()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(
            MakeUser(tag, "1"), MakeUser(tag, "2"), MakeUser(tag, "3"),
            MakeUser(tag, "4"), MakeUser(tag, "5"));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserListService>();

            var result = await service.GetUsersAsync(filter: tag, pageNumber: 2, pageSize: 2);

            Assert.Equal(2, result.Items.Count);
            Assert.Equal(5, result.TotalCount);
            Assert.Equal(2, result.PageNumber);
        });
    }

    [Fact]
    public async Task GetUsersAsync_PageNumberBeyondLastPage_ReturnsEmptyItemsWithCorrectTotalCount()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeUser(tag, "1"), MakeUser(tag, "2"));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserListService>();

            var result = await service.GetUsersAsync(filter: tag, pageNumber: 99, pageSize: 10);

            Assert.Empty(result.Items);
            Assert.Equal(2, result.TotalCount);
            Assert.Equal(99, result.PageNumber);
        });
    }

    [Fact]
    public async Task GetUsersAsync_UserLockedOut_MapsIsLockedOutTrue()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeUser(tag, "locked", lockedOut: true));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserListService>();

            var result = await service.GetUsersAsync(filter: tag, pageNumber: 1, pageSize: 10);

            Assert.True(Assert.Single(result.Items).IsLockedOut);
        });
    }

    [Fact]
    public async Task UnlockUserAsync_LockedUser_ClearsLockoutEnd()
    {
        var tag = Guid.NewGuid().ToString("N");
        var user = MakeUser(tag, "unlocktarget", lockedOut: true);
        await SeedAsync(user);

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IUserListService>();

            var result = await service.UnlockUserAsync(user.Id);

            Assert.Equal(UserUnlockStatus.Succeeded, result.Status);

            var listResult = await service.GetUsersAsync(filter: tag, pageNumber: 1, pageSize: 10);
            Assert.False(Assert.Single(listResult.Items).IsLockedOut);
        });
    }

}
