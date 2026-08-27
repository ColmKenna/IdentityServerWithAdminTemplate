using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Services.Users;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Users;

/// <summary>
///     Integration tests for <see cref="UserListService" /> exercised against the
///     shared SQLite in-memory database provided by <see cref="AdminWebFactory" />.
/// </summary>
public class UserListServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public UserListServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static ApplicationUser MakeUser(string tag, string suffix, bool lockedOut = false) => new()
    {
        Id = $"{tag}-user-{suffix}",
        UserName = $"{tag}-username-{suffix}",
        NormalizedUserName = $"{tag}-username-{suffix}".ToUpperInvariant(),
        Email = $"{tag}-email-{suffix}@sales.local",
        NormalizedEmail = $"{tag}-email-{suffix}@sales.local".ToUpperInvariant(),
        FullName = $"Full Name {suffix}",
        EmailConfirmed = true,
        LockoutEnabled = true,
        LockoutEnd = lockedOut ? DateTimeOffset.UtcNow.AddDays(1) : null,
        SecurityStamp = Guid.NewGuid().ToString("D")
    };

    private async Task SeedAsync(params ApplicationUser[] users)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            ApplicationDbContext db = sp.GetRequiredService<ApplicationDbContext>();
            db.Users.AddRange(users);
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GetUsersAsync_FilterMatchesUserName_ReturnsOnlyMatchingUser()
    {
        string tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeUser(tag, "alpha"), MakeUser(tag, "beta"));

        await _factory.RunInScopeAsync(async sp =>
        {
            IUserListService service = sp.GetRequiredService<IUserListService>();

            ListResult<UserListItem> result =
                await service.GetUsersAsync(new ListQuery($"{tag}-username-alpha", Pagination.From(1, 10)));

            UserListItem item = Assert.Single(result.Items);
            Assert.Equal($"{tag}-user-alpha", item.Id);
        });
    }

    [Fact]
    public async Task GetUsersAsync_FilterMatchesEmail_ReturnsOnlyMatchingUser()
    {
        string tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeUser(tag, "gamma"), MakeUser(tag, "delta"));

        await _factory.RunInScopeAsync(async sp =>
        {
            IUserListService service = sp.GetRequiredService<IUserListService>();

            ListResult<UserListItem> result =
                await service.GetUsersAsync(new ListQuery($"{tag}-email-delta", Pagination.From(1, 10)));

            UserListItem item = Assert.Single(result.Items);
            Assert.Equal($"{tag}-user-delta", item.Id);
        });
    }

    [Fact]
    public async Task GetUsersAsync_Pagination_ReturnsCorrectPageAndTotalCount()
    {
        string tag = Guid.NewGuid().ToString("N");
        await SeedAsync(
            MakeUser(tag, "1"), MakeUser(tag, "2"), MakeUser(tag, "3"),
            MakeUser(tag, "4"), MakeUser(tag, "5"));

        await _factory.RunInScopeAsync(async sp =>
        {
            IUserListService service = sp.GetRequiredService<IUserListService>();

            ListResult<UserListItem> result = await service.GetUsersAsync(new ListQuery(tag, Pagination.From(2, 2)));

            Assert.Equal(2, result.Items.Count);
            Assert.Equal(5, result.TotalCount);
            Assert.Equal(2, result.PageNumber);
        });
    }

    [Fact]
    public async Task GetUsersAsync_PageNumberBeyondLastPage_ReturnsEmptyItemsWithCorrectTotalCount()
    {
        string tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeUser(tag, "1"), MakeUser(tag, "2"));

        await _factory.RunInScopeAsync(async sp =>
        {
            IUserListService service = sp.GetRequiredService<IUserListService>();

            ListResult<UserListItem> result = await service.GetUsersAsync(new ListQuery(tag, Pagination.From(99, 10)));

            Assert.Empty(result.Items);
            Assert.Equal(2, result.TotalCount);
            Assert.Equal(99, result.PageNumber);
        });
    }

    [Fact]
    public async Task GetUsersAsync_UserLockedOut_MapsIsLockedOutTrue()
    {
        string tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeUser(tag, "locked", true));

        await _factory.RunInScopeAsync(async sp =>
        {
            IUserListService service = sp.GetRequiredService<IUserListService>();

            ListResult<UserListItem> result = await service.GetUsersAsync(new ListQuery(tag, Pagination.From(1, 10)));

            Assert.True(Assert.Single(result.Items).IsLockedOut);
        });
    }

    [Fact]
    public async Task UnlockUserAsync_LockedUser_ClearsLockoutEnd()
    {
        string tag = Guid.NewGuid().ToString("N");
        ApplicationUser user = MakeUser(tag, "unlocktarget", true);
        await SeedAsync(user);

        await _factory.RunInScopeAsync(async sp =>
        {
            IUserListService service = sp.GetRequiredService<IUserListService>();

            UserUnlockResult result = await service.UnlockUserAsync(UserId.Create(user.Id));

            Assert.Equal(UserUnlockStatus.Succeeded, result.Status);

            ListResult<UserListItem> listResult =
                await service.GetUsersAsync(new ListQuery(tag, Pagination.From(1, 10)));
            Assert.False(Assert.Single(listResult.Items).IsLockedOut);
        });
    }
}