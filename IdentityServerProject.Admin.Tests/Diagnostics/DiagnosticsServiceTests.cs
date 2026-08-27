using System.Security.Claims;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Services.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IdentityServerProject.Admin.Tests.Diagnostics;

/// <summary>
///     Exercises <see cref="DiagnosticsService" /> against the real (SQLite in-memory) DI container.
///     Covers the healthy path for all three stores plus signing key material; a genuine store
///     failure is exercised at the integration-test level instead (mocked IDiagnosticsService),
///     since forcing a connection failure against the shared test fixture isn't practical.
/// </summary>
public class DiagnosticsServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public DiagnosticsServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetDiagnosticsAsync_AllStoresConnectable_ReportsHealthyForEachStore()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IDiagnosticsService service = sp.GetRequiredService<IDiagnosticsService>();
            DiagnosticsModel result = await service.GetDiagnosticsAsync();

            Assert.Equal(3, result.StoreHealth.Count);
            Assert.All(result.StoreHealth, s => Assert.True(s.IsHealthy));
            Assert.Contains(result.StoreHealth, s => s.Name == "Identity Store");
            Assert.Contains(result.StoreHealth, s => s.Name == "Configuration Store");
            Assert.Contains(result.StoreHealth, s => s.Name == "Operational Store");
        });
    }

    [Fact]
    public async Task GetDiagnosticsAsync_ReturnsActiveSigningKeyAndValidationKeys()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IDiagnosticsService service = sp.GetRequiredService<IDiagnosticsService>();
            DiagnosticsModel result = await service.GetDiagnosticsAsync();

            Assert.False(string.IsNullOrWhiteSpace(result.SigningKeyId));
            Assert.False(string.IsNullOrWhiteSpace(result.SigningAlgorithm));

            Assert.NotEmpty(result.ActiveValidationKeys);
            Assert.Contains(result.ActiveValidationKeys, k => k.KeyId == result.SigningKeyId);
        });
    }

    [Fact]
    public async Task GetDiagnosticsAsync_UserHoldingReservedClaim_IsReportedForRemediation()
    {
        // Written straight through UserManager, i.e. the way a claim could have arrived before the
        // admin editor started refusing reserved types.
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = $"{tag}-reserved-claim-holder",
                Email = $"{tag}@sales.local",
                FullName = "Reserved Claim Holder"
            };
            Assert.True((await userManager.CreateAsync(user, "Password123!")).Succeeded);

            // The role claim type this deployment actually authorizes on - Duende's
            // AddAspNetIdentity rewrites it to the JWT short form, so it is not ClaimTypes.Role.
            string roleClaimType =
                sp.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity.RoleClaimType;
            await userManager.AddClaimAsync(user, new Claim(roleClaimType, Config.SysAdminRole));
            await userManager.AddClaimAsync(user, new Claim("dept", "Engineering"));

            IDiagnosticsService service = sp.GetRequiredService<IDiagnosticsService>();
            DiagnosticsModel result = await service.GetDiagnosticsAsync();

            ReservedClaimHolder holder = Assert.Single(result.ReservedClaimHolders, h => h.UserId == user.Id);
            Assert.Equal(roleClaimType, holder.ClaimType);
            Assert.Equal(Config.SysAdminRole, holder.ClaimValue);
            Assert.Equal(user.UserName, holder.UserName);

            // The ordinary claim on the same user must not be reported.
            Assert.DoesNotContain(result.ReservedClaimHolders, h => h.ClaimType == "dept");
        });
    }

    [Fact]
    public async Task GetDiagnosticsAsync_OnlyOrdinaryClaims_ReportsNoReservedHolders()
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = $"{tag}-ordinary-claim-holder",
                Email = $"{tag}@sales.local",
                FullName = "Ordinary Claim Holder"
            };
            Assert.True((await userManager.CreateAsync(user, "Password123!")).Succeeded);
            await userManager.AddClaimAsync(user, new Claim("team", "Platform"));

            IDiagnosticsService service = sp.GetRequiredService<IDiagnosticsService>();
            DiagnosticsModel result = await service.GetDiagnosticsAsync();

            Assert.DoesNotContain(result.ReservedClaimHolders, h => h.UserId == user.Id);
        });
    }
}