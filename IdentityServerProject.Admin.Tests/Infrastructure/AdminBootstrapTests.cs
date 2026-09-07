using IdentityServerProject.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

public class AdminBootstrapTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public AdminBootstrapTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DeletedAdmin_IsNotResurrectedOnHostStartup()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // Ensure the role exists (the host no longer seeds it unconditionally)
        if (!await roleManager.RoleExistsAsync(Config.SysAdminRole))
            await roleManager.CreateAsync(new IdentityRole(Config.SysAdminRole));

        string adminEmail = "admin-revocation-test@sales.local";
        var user = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FullName = "Revocation Test Admin"
        };
        await userManager.CreateAsync(user, "Password123!");
        await userManager.AddToRoleAsync(user, Config.SysAdminRole);

        // Delete user
        await userManager.DeleteAsync(user);
        Assert.Null(await userManager.FindByEmailAsync(adminEmail));

        // Start a second scope (simulating subsequent request / verification)
        using IServiceScope secondScope = _factory.Services.CreateScope();
        var secondUserManager = secondScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await secondUserManager.FindByEmailAsync(adminEmail));
    }

    [Fact]
    public async Task DemotedAdmin_IsNotRePromotedOnHostStartup()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // Ensure the role exists (the host no longer seeds it unconditionally)
        if (!await roleManager.RoleExistsAsync(Config.SysAdminRole))
            await roleManager.CreateAsync(new IdentityRole(Config.SysAdminRole));

        string adminEmail = "admin-demotion-test@sales.local";
        var user = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FullName = "Demotion Test Admin"
        };
        await userManager.CreateAsync(user, "Password123!");
        await userManager.AddToRoleAsync(user, Config.SysAdminRole);

        // Demote — remove the role
        await userManager.RemoveFromRoleAsync(user, Config.SysAdminRole);
        Assert.False(await userManager.IsInRoleAsync(user, Config.SysAdminRole));

        // Verify in a second scope that the demotion persists
        using IServiceScope secondScope = _factory.Services.CreateScope();
        var secondUserManager = secondScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser? reloadedUser = await secondUserManager.FindByEmailAsync(adminEmail);
        Assert.NotNull(reloadedUser);
        Assert.False(await secondUserManager.IsInRoleAsync(reloadedUser, Config.SysAdminRole));
    }
}

