using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.IdentityResources;

/// <summary>
///     Covers the seed half of the protection: <c>NonEditable</c> was previously read in five places
///     and written in none, so it defaulted to <c>false</c> on every seeded row.
/// </summary>
/// <remarks>
///     Each test gets its own factory (and therefore its own database), because these tests run the
///     real seeder and assert on the exact contents of the identity resource table.
/// </remarks>
public class SeededIdentityResourceProtectionTests
{
    private static async Task SeedAsync(AdminWebFactory factory)
    {
        await factory.RunInScopeAsync(async sp =>
        {
            var clients = new List<SeedClientSpec>
            {
                new("razorclient", "Sales Razor Client", AbsoluteHttpUri.Create("https://localhost:5001"), "secret"),
                new("blazorclient", "Sales Blazor Client", AbsoluteHttpUri.Create("https://localhost:5002"), "secret")
            };

            await SeedData.SeedAsync(
                sp.GetRequiredService<ApplicationDbContext>(),
                sp.GetRequiredService<ConfigurationDbContext>(),
                sp.GetRequiredService<PersistedGrantDbContext>(),
                sp.GetRequiredService<UserManager<ApplicationUser>>(),
                sp.GetRequiredService<RoleManager<IdentityRole>>(),
                clients,
                "admin@sales.local",
                "Password123!",
                "Password123!",
                TokenLifetimes.Default);
        });
    }

    private static async Task<IdentityResource?> LoadAsync(AdminWebFactory factory, string name)
    {
        IdentityResource? resource = null;
        await factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            resource = await configDb.IdentityResources
                .AsNoTracking()
                .Include(r => r.UserClaims)
                .FirstOrDefaultAsync(r => r.Name == name);
        });
        return resource;
    }

    [Fact]
    public async Task Seed_MarksTheOpenIdResourceNonEditable()
    {
        using var factory = new AdminWebFactory();
        await SeedAsync(factory);

        IdentityResource? openId = await LoadAsync(factory, "openid");

        Assert.NotNull(openId);
        Assert.True(openId!.NonEditable);
        Assert.Contains(openId.UserClaims, c => c.Type == "sub");
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("email")]
    [InlineData("roles")]
    public async Task Seed_LeavesTheOtherSeededResourcesEditable(string name)
    {
        // Deliberate: these are standard but not protocol-mandatory, and operators legitimately
        // curate which claims they return. Protecting them would cost capability for no security.
        using var factory = new AdminWebFactory();
        await SeedAsync(factory);

        IdentityResource? resource = await LoadAsync(factory, name);

        Assert.NotNull(resource);
        Assert.False(resource!.NonEditable);
    }

    [Fact]
    public async Task Seed_RepairsAnOpenIdRowThatPredatesTheProtection()
    {
        // The realistic upgrade case. Every existing deployment has this row with NonEditable
        // = false, and EnsureCreated leaves no migration path that would fix it (DB-001).
        using var factory = new AdminWebFactory();

        await factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.IdentityResources.Add(new IdentityResource
            {
                Name = "openid",
                DisplayName = "Your user identifier",
                Enabled = true,
                NonEditable = false,
                UserClaims = new List<IdentityResourceClaim> { new() { Type = "sub" } }
            });
            await configDb.SaveChangesAsync();
        });

        await SeedAsync(factory);

        IdentityResource? openId = await LoadAsync(factory, "openid");
        Assert.NotNull(openId);
        Assert.True(openId!.NonEditable);
    }

    [Fact]
    public async Task Seed_IsIdempotent_AndDoesNotDuplicateResources()
    {
        using var factory = new AdminWebFactory();
        await SeedAsync(factory);
        await SeedAsync(factory);

        await factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            Assert.Equal(1, await configDb.IdentityResources.CountAsync(r => r.Name == "openid"));
        });

        Assert.True((await LoadAsync(factory, "openid"))!.NonEditable);
    }
}