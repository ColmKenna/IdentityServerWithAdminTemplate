using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Users;

public class UserCreateServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public UserCreateServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static UserCreateInputModel MakeInput(string tag) => new()
    {
        UserName = $"{tag}-username",
        Email = $"{tag}-email@sales.local",
        FullName = $"{tag} Full Name",
        Password = "Password123!",
        ConfirmPassword = "Password123!"
    };

    [Fact]
    public async Task CreateUserAsync_ValidInput_CreatesUserAndReturnsSuccess()
    {
        string tag = Guid.NewGuid().ToString("N");
        UserCreateInputModel input = MakeInput(tag);

        await _factory.RunInScopeAsync(async sp =>
        {
            IUserCreateService service = sp.GetRequiredService<IUserCreateService>();
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

            UserCreateResult result = await service.CreateUserAsync(input);

            Assert.True(result.Success);
            Assert.False(string.IsNullOrWhiteSpace(result.UserId));

            ApplicationUser? created = await userManager.FindByIdAsync(result.UserId!);
            Assert.NotNull(created);
            Assert.Equal(input.UserName, created!.UserName);
            Assert.Equal(input.Email, created.Email);
            Assert.Equal(input.FullName, created.FullName);
            Assert.True(created.EmailConfirmed);
        });
    }

    [Fact]
    public async Task CreateUserAsync_DuplicateUserName_ReturnsFailureWithoutCreatingSecondUser()
    {
        string tag = Guid.NewGuid().ToString("N");
        UserCreateInputModel firstInput = MakeInput(tag);

        await _factory.RunInScopeAsync(async sp =>
        {
            IUserCreateService service = sp.GetRequiredService<IUserCreateService>();
            UserCreateResult firstResult = await service.CreateUserAsync(firstInput);
            Assert.True(firstResult.Success);

            UserCreateInputModel duplicateInput = MakeInput(tag);
            duplicateInput.Email = $"{tag}-different-email@sales.local"; // same username, different email

            UserCreateResult duplicateResult = await service.CreateUserAsync(duplicateInput);

            Assert.False(duplicateResult.Success);
            Assert.Contains(duplicateResult.Errors,
                e => e.Contains("already taken", StringComparison.OrdinalIgnoreCase) ||
                     e.Contains("Username", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public async Task CreateUserAsync_DuplicateEmail_ReturnsFailureWithoutCreatingSecondUser()
    {
        string tag = Guid.NewGuid().ToString("N");
        UserCreateInputModel firstInput = MakeInput(tag);

        await _factory.RunInScopeAsync(async sp =>
        {
            IUserCreateService service = sp.GetRequiredService<IUserCreateService>();
            UserCreateResult firstResult = await service.CreateUserAsync(firstInput);
            Assert.True(firstResult.Success);

            UserCreateInputModel duplicateInput = MakeInput(tag);
            duplicateInput.UserName = $"{tag}-different-username"; // different username, same email

            UserCreateResult duplicateResult = await service.CreateUserAsync(duplicateInput);

            Assert.False(duplicateResult.Success);
            Assert.Contains(duplicateResult.Errors, e => e.Contains("Email", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public async Task CreateUserAsync_WeakPassword_ReturnsFailure()
    {
        string tag = Guid.NewGuid().ToString("N");
        UserCreateInputModel input = MakeInput(tag);
        input.Password = "weak";
        input.ConfirmPassword = "weak";

        await _factory.RunInScopeAsync(async sp =>
        {
            IUserCreateService service = sp.GetRequiredService<IUserCreateService>();
            UserCreateResult result = await service.CreateUserAsync(input);

            Assert.False(result.Success);
            Assert.NotEmpty(result.Errors);
        });
    }
}