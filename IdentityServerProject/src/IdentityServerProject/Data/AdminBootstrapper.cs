using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IdentityServerProject.Data;

public static class AdminBootstrapper
{
    public static async Task BootstrapSysAdminAsync(IServiceProvider services, IConfiguration configuration)
    {
        using IServiceScope scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(AdminBootstrapper));

        string? email = configuration["AdminBootstrap:Email"] ?? configuration["Seed:SysAdminEmail"];
        string? password = configuration["AdminBootstrap:Password"] ?? configuration["Seed:SysAdminPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogError("Admin bootstrap aborted: 'AdminBootstrap:Email' and 'AdminBootstrap:Password' must be configured.");
            return;
        }

        if (!await roleManager.RoleExistsAsync(Config.SysAdminRole))
        {
            IdentityResult roleResult = await roleManager.CreateAsync(new IdentityRole(Config.SysAdminRole));
            if (!roleResult.Succeeded)
            {
                logger.LogError("Admin bootstrap failed to create role '{Role}': {Errors}",
                    Config.SysAdminRole, string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                return;
            }
        }

        ApplicationUser? existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser != null)
        {
            logger.LogWarning("Admin bootstrap skipped: User '{Email}' already exists. Refusing to alter existing credentials or roles.", email);
            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = "System Administrator"
        };

        IdentityResult result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError("Admin bootstrap failed to create user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(user, Config.SysAdminRole);
        logger.LogInformation("Administrator account '{Email}' successfully created via one-time bootstrap.", email);
    }
}

