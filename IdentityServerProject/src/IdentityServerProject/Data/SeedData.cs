using System.Security.Claims;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Duende.IdentityServer.Models;
using IdentityServerProject.Services.IdentityResources;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using IdentityResource = Duende.IdentityServer.EntityFramework.Entities.IdentityResource;

namespace IdentityServerProject.Data;

/// <summary>
///     Seeds the configured system administrator in every environment, and the sample
///     clients, resources, and test user when development seeding is requested. Every
///     step is idempotent, so re-running against an already-seeded database is a no-op.
/// </summary>
public static class SeedData
{
    public static async Task SeedAsync(
        ApplicationDbContext identityDb,
        ConfigurationDbContext configDb,
        PersistedGrantDbContext operationalDb,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IReadOnlyList<SeedClientSpec> clients,
        string sysAdminEmail,
        string sysAdminPassword,
        string testUserPassword)
    {
        await SeedRolesAndUsersAsync(identityDb, userManager, roleManager, sysAdminEmail, sysAdminPassword,
            testUserPassword);
        await SeedClientsAsync(configDb, clients);
        await SeedResourcesAsync(configDb);
    }

    public static async Task SeedSysAdminAsync(
        ApplicationDbContext identityDb,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        string sysAdminEmail,
        string sysAdminPassword)
    {
        if (!await roleManager.RoleExistsAsync(Config.SysAdminRole))
        {
            IdentityResult roleResult = await roleManager.CreateAsync(new IdentityRole(Config.SysAdminRole));
            if (!roleResult.Succeeded)
                throw new InvalidOperationException(
                    $"Failed to seed role '{Config.SysAdminRole}': {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");
        }

        await EnsureUserAsync(userManager, sysAdminEmail, "System Administrator", sysAdminPassword,
            Config.SysAdminRole);
    }

    private static async Task SeedRolesAndUsersAsync(
        ApplicationDbContext identityDb,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        string sysAdminEmail,
        string sysAdminPassword,
        string testUserPassword)
    {
        await SeedSysAdminAsync(
            identityDb,
            userManager,
            roleManager,
            sysAdminEmail,
            sysAdminPassword);
        await EnsureUserAsync(userManager, "testuser@sales.local", "Test User", testUserPassword, null);
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string fullName,
        string password,
        string? role)
    {
        ApplicationUser? user = await userManager.FindByNameAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName
            };

            IdentityResult result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Failed to seed user '{email}': {string.Join(", ", result.Errors.Select(e => e.Description))}");

            await userManager.AddClaimsAsync(user, new[]
            {
                new Claim("name", fullName)
            });
        }

        if (role is not null && !await userManager.IsInRoleAsync(user, role))
            await userManager.AddToRoleAsync(user, role);
    }

    private static async Task SeedClientsAsync(
        ConfigurationDbContext configDb,
        IReadOnlyList<SeedClientSpec> clients)
    {
        List<string> existingClientIds = await configDb.Clients.Select(c => c.ClientId).ToListAsync();

        foreach (Client client in Config.Clients(clients))
            if (!existingClientIds.Contains(client.ClientId))
                configDb.Clients.Add(client.ToEntity());

        await configDb.SaveChangesAsync();
    }

    private static async Task SeedResourcesAsync(ConfigurationDbContext configDb)
    {
        // Tracked, not projected to names: an already-seeded row may need its protection flag
        // repaired below. The table holds a handful of rows, so loading it is not a cost.
        List<IdentityResource> existingIdentityResources = await configDb.IdentityResources.ToListAsync();
        foreach (Duende.IdentityServer.Models.IdentityResource resource in Config.IdentityResources)
        {
            IdentityResource? existing = existingIdentityResources.FirstOrDefault(r => r.Name == resource.Name);

            if (existing is null)
            {
                IdentityResource entity = resource.ToEntity();

                // ToEntity() does not carry NonEditable — it has no counterpart on the Duende model
                // — so protection has to be stamped here or every seeded row lands unprotected.
                entity.NonEditable = BuiltInIdentityResourcePolicy.IsProtectedName(entity.Name);
                configDb.IdentityResources.Add(entity);
            }
            else if (BuiltInIdentityResourcePolicy.IsProtectedName(existing.Name) && !existing.NonEditable)
                // Repairs databases seeded before this protection existed. The guard does not
                // depend on the flag being right, but the flag drives the read-only UI and the
                // list page's badge, so leaving it stale would misreport the resource as editable.
                existing.NonEditable = true;
        }

        List<string> existingApiScopes = await configDb.ApiScopes.Select(s => s.Name).ToListAsync();
        foreach (ApiScope scope in Config.ApiScopes)
            if (!existingApiScopes.Contains(scope.Name))
                configDb.ApiScopes.Add(scope.ToEntity());

        List<string> existingApiResources = await configDb.ApiResources.Select(r => r.Name).ToListAsync();
        foreach (ApiResource resource in Config.ApiResources)
            if (!existingApiResources.Contains(resource.Name))
                configDb.ApiResources.Add(resource.ToEntity());

        await configDb.SaveChangesAsync();
    }
}