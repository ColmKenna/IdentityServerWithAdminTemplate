using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Duende.IdentityServer.EntityFramework.Mappers;
using IdentityServerProject.Services.IdentityResources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.IdentityResources;

/// <summary>
/// Exercises <see cref="IdentityResourceEditorService"/> against a real (SQLite in-memory) DI
/// container.
/// </summary>
/// <remarks>
/// The built-in resource under test is materialised from Duende's own
/// <c>IdentityResources.OpenId()</c> rather than a hand-built lookalike, because the defect this
/// suite guards against was precisely that the protection matched a claim literal instead of the
/// real resource. A fabricated row named "openid" would have passed the old code too.
/// </remarks>
public class IdentityResourceEditorServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public IdentityResourceEditorServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Seeds the genuine built-in openid resource if this shared connection does not have it yet.
    /// Idempotent: every test here is a rejection test, so the row stays in its seeded state.
    /// </summary>
    private async Task EnsureBuiltInOpenIdResourceAsync()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            if (await configDb.IdentityResources.AnyAsync(r => r.Name == "openid"))
            {
                return;
            }

            var entity = new Duende.IdentityServer.Models.IdentityResources.OpenId().ToEntity();
            entity.NonEditable = true;
            configDb.IdentityResources.Add(entity);
            await configDb.SaveChangesAsync();
        });
    }

    private async Task<string> CreateCustomResourceAsync(bool nonEditable, params string[] claims)
    {
        var name = $"custom-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.IdentityResources.Add(new IdentityResource
            {
                Name = name,
                DisplayName = "Custom Resource",
                Enabled = true,
                NonEditable = nonEditable,
                UserClaims = claims.Select(c => new IdentityResourceClaim { Type = c }).ToList(),
            });
            await configDb.SaveChangesAsync();
        });

        return name;
    }

    private async Task<IdentityResource> LoadAsync(string name)
    {
        IdentityResource? loaded = null;
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            loaded = await configDb.IdentityResources
                .AsNoTracking()
                .Include(r => r.UserClaims)
                .FirstOrDefaultAsync(r => r.Name == name);
        });

        Assert.NotNull(loaded);
        return loaded!;
    }

    #region The genuine built-in openid resource

    [Fact]
    public void TheBuiltInOpenIdResource_CarriesSubAsARequiredResource()
    {
        // Pins the two facts every other test in this region depends on. If Duende ever changed
        // either, the protection below would still pass while guarding the wrong thing.
        var model = new Duende.IdentityServer.Models.IdentityResources.OpenId();

        Assert.Equal("openid", model.Name);
        Assert.Contains("sub", model.UserClaims);
        Assert.True(model.Required);
    }

    [Fact]
    public async Task UpdateBasicsAsync_BuiltInOpenIdResource_IsRefusedAndNothingChanges()
    {
        await EnsureBuiltInOpenIdResourceAsync();
        var before = await LoadAsync("openid");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceEditorService>();

            var result = await service.UpdateBasicsAsync(
                "openid",
                displayName: "Hijacked",
                description: "Hijacked",
                enabled: false,
                required: false,
                emphasize: false,
                showInDiscoveryDocument: false);

            Assert.Equal(IdentityResourceEditOutcome.Protected, result.Outcome);
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        });

        var after = await LoadAsync("openid");
        Assert.Equal(before.DisplayName, after.DisplayName);
        Assert.True(after.Enabled);
        Assert.True(after.ShowInDiscoveryDocument);
    }

    [Fact]
    public async Task UpdateBasicsAsync_BuiltInOpenIdResource_CannotBeDisabled()
    {
        // Called out separately from the test above because disabling is the single change that
        // breaks every client at once, and it is reachable from one checkbox in the editor.
        await EnsureBuiltInOpenIdResourceAsync();

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceEditorService>();
            var result = await service.UpdateBasicsAsync(
                "openid", "OpenId", null, enabled: false, required: true, emphasize: false, showInDiscoveryDocument: true);

            Assert.Equal(IdentityResourceEditOutcome.Protected, result.Outcome);
        });

        Assert.True((await LoadAsync("openid")).Enabled);
    }

    [Fact]
    public async Task AddClaimAsync_BuiltInOpenIdResource_IsRefusedAndNoClaimIsStored()
    {
        // The hole the previous implementation left open: AddClaimAsync was the one mutation that
        // never consulted NonEditable, so a protected resource could be broadened and then never
        // narrowed again.
        await EnsureBuiltInOpenIdResourceAsync();

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceEditorService>();
            var result = await service.AddClaimAsync("openid", "email");

            Assert.Equal(IdentityResourceEditOutcome.Protected, result.Outcome);
        });

        Assert.DoesNotContain((await LoadAsync("openid")).UserClaims, c => c.Type == "email");
    }

    [Fact]
    public async Task RemoveClaimAsync_SubFromBuiltInOpenIdResource_IsRefusedAndSubSurvives()
    {
        await EnsureBuiltInOpenIdResourceAsync();

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceEditorService>();
            var result = await service.RemoveClaimAsync("openid", "sub");

            Assert.Equal(IdentityResourceEditOutcome.Protected, result.Outcome);

            // The specific reason, not the generic one — the operator is told what sub is for.
            Assert.Contains("sub", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("required", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        });

        Assert.Contains((await LoadAsync("openid")).UserClaims, c => c.Type == "sub");
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_BuiltInOpenIdResource_IsBlocked()
    {
        await EnsureBuiltInOpenIdResourceAsync();

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceListService>();
            var result = await service.DeleteIdentityResourceAsync("openid");

            Assert.Equal(IdentityResourceDeleteResult.Blocked, result);
        });

        await LoadAsync("openid"); // asserts it still exists
    }

    #endregion

    #region NonEditable is honoured identically by all three mutations

    [Fact]
    public async Task AllThreeMutations_RejectANonEditableResource_Identically()
    {
        // The three methods drifted apart once already. Asserting them together, rather than in
        // three separate tests, is what makes a future divergence fail loudly.
        var name = await CreateCustomResourceAsync(nonEditable: true, "email");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceEditorService>();

            var outcomes = new[]
            {
                (await service.UpdateBasicsAsync(name, "x", "x", true, false, false, true)).Outcome,
                (await service.AddClaimAsync(name, "phone_number")).Outcome,
                (await service.RemoveClaimAsync(name, "email")).Outcome,
            };

            Assert.All(outcomes, o => Assert.Equal(IdentityResourceEditOutcome.Protected, o));
        });

        var after = await LoadAsync(name);
        Assert.Equal("Custom Resource", after.DisplayName);
        Assert.DoesNotContain(after.UserClaims, c => c.Type == "phone_number");
        Assert.Contains(after.UserClaims, c => c.Type == "email");
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_NonEditableResource_IsBlocked()
    {
        var name = await CreateCustomResourceAsync(nonEditable: true);

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceListService>();
            Assert.Equal(IdentityResourceDeleteResult.Blocked, await service.DeleteIdentityResourceAsync(name));
        });

        await LoadAsync(name);
    }

    #endregion

    #region Ordinary resources stay fully editable

    [Fact]
    public async Task UpdateBasicsAsync_OrdinaryResource_Succeeds()
    {
        var name = await CreateCustomResourceAsync(nonEditable: false);

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceEditorService>();
            var result = await service.UpdateBasicsAsync(
                name, "Renamed", "New description", enabled: false, required: true, emphasize: true, showInDiscoveryDocument: false);

            Assert.Equal(IdentityResourceEditOutcome.Success, result.Outcome);
            Assert.Null(result.ErrorMessage);
        });

        var after = await LoadAsync(name);
        Assert.Equal("Renamed", after.DisplayName);
        Assert.False(after.Enabled);
        Assert.True(after.Emphasize);
    }

    [Fact]
    public async Task AddAndRemoveClaimAsync_OrdinaryResource_Succeed()
    {
        var name = await CreateCustomResourceAsync(nonEditable: false, "email");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceEditorService>();
            Assert.Equal(IdentityResourceEditOutcome.Success, (await service.AddClaimAsync(name, "phone_number")).Outcome);
            Assert.Equal(IdentityResourceEditOutcome.Success, (await service.RemoveClaimAsync(name, "email")).Outcome);
        });

        var after = await LoadAsync(name);
        Assert.Contains(after.UserClaims, c => c.Type == "phone_number");
        Assert.DoesNotContain(after.UserClaims, c => c.Type == "email");
    }

    [Fact]
    public async Task RemoveClaimAsync_SubFromAnOrdinaryResource_Succeeds()
    {
        // The sub invariant is scoped to the openid resource, not to the claim type globally.
        // A custom resource that happens to carry sub stays editable.
        var name = await CreateCustomResourceAsync(nonEditable: false, "sub", "email");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceEditorService>();
            Assert.Equal(IdentityResourceEditOutcome.Success, (await service.RemoveClaimAsync(name, "sub")).Outcome);
        });

        Assert.DoesNotContain((await LoadAsync(name)).UserClaims, c => c.Type == "sub");
    }

    #endregion

    #region Missing resources are still reported as missing

    [Theory]
    [InlineData("basics")]
    [InlineData("add")]
    [InlineData("remove")]
    public async Task Mutations_OnAMissingResource_ReportNotFoundRatherThanProtected(string operation)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceEditorService>();
            var missing = $"missing-{Guid.NewGuid():N}";

            var result = operation switch
            {
                "basics" => await service.UpdateBasicsAsync(missing, "x", null, true, false, false, true),
                "add" => await service.AddClaimAsync(missing, "email"),
                _ => await service.RemoveClaimAsync(missing, "email"),
            };

            Assert.Equal(IdentityResourceEditOutcome.NotFound, result.Outcome);
        });
    }

    #endregion

    [Fact]
    public async Task CreateAsync_NewResource_PersistsAllEditorFieldsAndClaims()
    {
        var name = $"{Guid.NewGuid():N}-resource";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceEditorService>();
            var result = await service.CreateAsync(
                name,
                "Resource display",
                "Resource description",
                enabled: false,
                required: true,
                emphasize: true,
                showInDiscoveryDocument: false,
                userClaims: new List<string> { "email", "name" });

            Assert.True(result.Success, result.ErrorMessage);
            var editor = await service.GetForEditAsync(name);
            Assert.NotNull(editor);
            Assert.Equal("Resource display", editor!.DisplayName);
            Assert.Equal("Resource description", editor.Description);
            Assert.False(editor.Enabled);
            Assert.True(editor.Required);
            Assert.True(editor.Emphasize);
            Assert.False(editor.ShowInDiscoveryDocument);
            Assert.Equal(new[] { "email", "name" }, editor.UserClaims.OrderBy(claim => claim));
        });
    }

    [Fact]
    public async Task CreateAsync_DuplicateClaims_PropagatesTheDatabaseConstraintFailure()
    {
        var name = $"{Guid.NewGuid():N}-duplicate-claims";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceEditorService>();

            await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(
                name,
                "Resource display",
                null,
                enabled: true,
                required: false,
                emphasize: false,
                showInDiscoveryDocument: true,
                userClaims: new List<string> { "email", "email" }));
        });
    }

    [Fact]
    public async Task CreateAsync_NameUsedByAnApiScope_ReturnsFailureWithoutCreatingAResource()
    {
        var name = $"{Guid.NewGuid():N}-shared";

        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            db.ApiScopes.Add(new ApiScope { Name = name });
            await db.SaveChangesAsync();

            var service = sp.GetRequiredService<IIdentityResourceEditorService>();
            var result = await service.CreateAsync(name, "Resource display", null, true, false, false, true, new List<string>());

            Assert.False(result.Success);
            Assert.Equal("An API scope with this name already exists.", result.ErrorMessage);
            Assert.False(await db.IdentityResources.AnyAsync(resource => resource.Name == name));
        });
    }
}
