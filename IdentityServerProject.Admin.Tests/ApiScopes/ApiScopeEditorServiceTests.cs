using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.ApiScopes;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.ApiScopes;

/// <summary>
/// Exercises <see cref="ApiScopeEditorService"/> against a real (SQLite in-memory)
/// <see cref="ConfigurationDbContext"/> resolved from the shared <see cref="AdminWebFactory"/> DI container.
/// Every test seeds with a unique tag embedded in the scope name so assertions are
/// unaffected by data left behind by other tests sharing the same connection.
/// </summary>
public class ApiScopeEditorServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ApiScopeEditorServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedApiScopeAsync(ApiScope scope)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiScopes.Add(scope);
            await configDb.SaveChangesAsync();
        });
    }

    // ---------- GetForEditAsync ----------

    [Fact]
    public async Task GetForEditAsync_ScopeExists_ReturnsPopulatedModelWithClaims()
    {
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-scope";
        await SeedApiScopeAsync(new ApiScope
        {
            Name = name,
            DisplayName = "Display",
            Description = "Desc",
            UserClaims = new List<ApiScopeClaim> { new ApiScopeClaim { Type = "email" } },
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeEditorService>();

            var editor = await service.GetForEditAsync(ScopeName.Create(name));

            Assert.NotNull(editor);
            Assert.Equal(name, editor.Name);
            Assert.Equal("Display", editor.DisplayName);
            Assert.Equal("Desc", editor.Description);
            Assert.Equal(new[] { "email" }, editor.Claims);
        });
    }

    [Fact]
    public async Task GetForEditAsync_ScopeDoesNotExist_ReturnsNull()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeEditorService>();

            var editor = await service.GetForEditAsync(ScopeName.Create(Guid.NewGuid().ToString("N")));

            Assert.Null(editor);
        });
    }

    // ---------- UpdateBasicsAsync ----------

    [Fact]
    public async Task UpdateBasicsAsync_ScopeExists_UpdatesDisplayNameAndDescriptionButNotName()
    {
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-scope";
        await SeedApiScopeAsync(new ApiScope { Name = name, DisplayName = "Old", Description = "Old desc" });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeEditorService>();

            var success = await service.UpdateBasicsAsync(name, "New", "New desc", true, false, false, true);

            Assert.True(success);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var scope = await configDb.ApiScopes.SingleAsync(s => s.Name == name);

            Assert.Equal(name, scope.Name);
            Assert.Equal("New", scope.DisplayName);
            Assert.Equal("New desc", scope.Description);
        });
    }

    [Fact]
    public async Task UpdateBasicsAsync_ScopeDoesNotExist_ReturnsFalse()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeEditorService>();

            var success = await service.UpdateBasicsAsync(Guid.NewGuid().ToString("N"), "New", "New desc", true, false, false, true);

            Assert.False(success);
        });
    }

    // ---------- AddClaimAsync ----------

    [Fact]
    public async Task AddClaimAsync_NewClaimType_AddsClaim()
    {
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-scope";
        await SeedApiScopeAsync(new ApiScope { Name = name });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeEditorService>();

            var success = await service.AddClaimAsync(ScopeName.Create(name), ClaimType.Create("email"));

            Assert.True(success);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var scope = await configDb.ApiScopes.Include(s => s.UserClaims).SingleAsync(s => s.Name == name);

            Assert.Contains(scope.UserClaims, c => c.Type == "email");
        });
    }

    [Fact]
    public async Task AddClaimAsync_DuplicateClaimType_IsNoOp()
    {
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-scope";
        await SeedApiScopeAsync(new ApiScope { Name = name, UserClaims = new List<ApiScopeClaim> { new ApiScopeClaim { Type = "email" } } });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeEditorService>();

            var success = await service.AddClaimAsync(ScopeName.Create(name), ClaimType.Create("email"));

            Assert.True(success);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var scope = await configDb.ApiScopes.Include(s => s.UserClaims).SingleAsync(s => s.Name == name);

            Assert.Single(scope.UserClaims);
        });
    }

    [Fact]
    public async Task AddClaimAsync_ScopeDoesNotExist_ReturnsFalse()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeEditorService>();

            var success = await service.AddClaimAsync(ScopeName.Create(Guid.NewGuid().ToString("N")), ClaimType.Create("email"));

            Assert.False(success);
        });
    }

    // ---------- RemoveClaimAsync ----------

    [Fact]
    public async Task RemoveClaimAsync_ExistingClaimType_RemovesClaim()
    {
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-scope";
        await SeedApiScopeAsync(new ApiScope { Name = name, UserClaims = new List<ApiScopeClaim> { new ApiScopeClaim { Type = "email" } } });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeEditorService>();

            var success = await service.RemoveClaimAsync(ScopeName.Create(name), ClaimType.Create("email"));

            Assert.True(success);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var scope = await configDb.ApiScopes.Include(s => s.UserClaims).SingleAsync(s => s.Name == name);

            Assert.Empty(scope.UserClaims);
        });
    }

    [Fact]
    public async Task RemoveClaimAsync_ClaimTypeNotPresent_ReturnsFalse()
    {
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-scope";
        await SeedApiScopeAsync(new ApiScope { Name = name });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeEditorService>();

            var success = await service.RemoveClaimAsync(ScopeName.Create(name), ClaimType.Create("email"));

            Assert.False(success);
        });
    }

    [Fact]
    public async Task RemoveClaimAsync_ScopeDoesNotExist_ReturnsFalse()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeEditorService>();

            var success = await service.RemoveClaimAsync(ScopeName.Create(Guid.NewGuid().ToString("N")), ClaimType.Create("email"));

            Assert.False(success);
        });
    }

    [Fact]
    public async Task CreateAsync_NewScope_PersistsTheSuppliedDisplayFieldsAndEnablesIt()
    {
        var name = $"{Guid.NewGuid():N}-scope";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeEditorService>();
            var result = await service.CreateAsync(name, "Scope display", "Scope description");

            Assert.True(result.Succeeded, result.ErrorMessage);
            var editor = await service.GetForEditAsync(ScopeName.Create(name));
            Assert.NotNull(editor);
            Assert.Equal("Scope display", editor!.DisplayName);
            Assert.Equal("Scope description", editor.Description);
            Assert.True(editor.Enabled);
        });
    }

    [Fact]
    public async Task CreateAsync_NameUsedByAnIdentityResource_ReturnsFailureWithoutCreatingAScope()
    {
        var name = $"{Guid.NewGuid():N}-shared";
        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            db.IdentityResources.Add(new IdentityResource { Name = name });
            await db.SaveChangesAsync();

            var service = sp.GetRequiredService<IApiScopeEditorService>();
            var result = await service.CreateAsync(name, "Scope display", null);

            Assert.False(result.Succeeded);
            Assert.Equal("An identity resource with this name already exists.", result.ErrorMessage);
            Assert.False(await db.ApiScopes.AnyAsync(scope => scope.Name == name));
        });
    }
}


