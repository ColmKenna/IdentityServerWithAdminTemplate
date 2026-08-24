using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.Apis;
using IdentityServerProject.Services.Validation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Apis;

/// <summary>
/// Exercises <see cref="ApiResourceEditorService"/> against a real (SQLite in-memory)
/// <see cref="ConfigurationDbContext"/> resolved from the shared <see cref="AdminWebFactory"/> DI container.
/// </summary>
public class ApiResourceEditorServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ApiResourceEditorServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedApiResourceAsync(ApiResource resource)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiResources.Add(resource);
            await configDb.SaveChangesAsync();
        });
    }

    // ---------- GetForEditAsync ----------

    [Fact]
    public async Task GetForEditAsync_ResourceExists_ReturnsPopulatedModel()
    {
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-api";
        await SeedApiResourceAsync(new ApiResource
        {
            Name = name,
            DisplayName = "Display",
            Description = "Desc",
            Enabled = true,
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var editor = await service.GetForEditAsync(name);

            Assert.NotNull(editor);
            Assert.False(editor!.IsNew);
            Assert.Equal(name, editor.Name);
            Assert.Equal("Display", editor.DisplayName);
            Assert.True(editor.Enabled);
        });
    }

    [Fact]
    public async Task GetForEditAsync_ResourceDoesNotExist_ReturnsNull()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var editor = await service.GetForEditAsync(Guid.NewGuid().ToString("N"));

            Assert.Null(editor);
        });
    }

    // ---------- SaveBasicsAsync ----------

    [Fact]
    public async Task SaveBasicsAsync_OriginalNameNull_CreatesNewApiResource()
    {
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-new";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var result = await service.SaveBasicsAsync(new SaveApiResourceBasicsCommand(null, name, "Display", "Desc"));

            Assert.True(result.Succeeded);
            var editor = await service.GetForEditAsync(name);
            Assert.NotNull(editor);
            Assert.True(editor!.Enabled);
        });
    }

    [Fact]
    public async Task SaveBasicsAsync_ExistingResourceRenamed_UpdatesNameAndFields()
    {
        var tag = Guid.NewGuid().ToString("N");
        var originalName = $"{tag}-original";
        var newName = $"{tag}-renamed";
        await SeedApiResourceAsync(new ApiResource { Name = originalName, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var result = await service.SaveBasicsAsync(new SaveApiResourceBasicsCommand(originalName, newName, "New Display", "New Desc"));

            Assert.True(result.Succeeded);
            Assert.Null(await service.GetForEditAsync(originalName));
            var renamed = await service.GetForEditAsync(newName);
            Assert.NotNull(renamed);
            Assert.Equal("New Display", renamed!.DisplayName);
        });
    }

    [Fact]
    public async Task SaveBasicsAsync_NameCollidesWithAnotherResource_ReturnsNameCollision()
    {
        var tag = Guid.NewGuid().ToString("N");
        var existingName = $"{tag}-existing";
        var otherName = $"{tag}-other";
        await SeedApiResourceAsync(new ApiResource { Name = existingName, Enabled = true });
        await SeedApiResourceAsync(new ApiResource { Name = otherName, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var result = await service.SaveBasicsAsync(new SaveApiResourceBasicsCommand(otherName, existingName, null, null));

            Assert.Equal(AdminMutationStatus.Conflict, result.Status);
        });
    }

    [Fact]
    public async Task SaveBasicsAsync_OriginalNameDoesNotExist_ReturnsNotFound()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var result = await service.SaveBasicsAsync(new SaveApiResourceBasicsCommand(Guid.NewGuid().ToString("N"), "irrelevant", null, null));

            Assert.Equal(AdminMutationStatus.NotFound, result.Status);
        });
    }

    // ---------- Negative / idempotent branch coverage ----------

    [Fact]
    public async Task AttachScopeAsync_ResourceDoesNotExist_ReturnsNotFound()
    {
        var tag = Guid.NewGuid().ToString("N");
        var scopeName = $"{tag}-scope";
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiScopes.Add(new ApiScope { Name = scopeName, Enabled = true });
            await configDb.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var result = await service.AttachScopeAsync($"{tag}-missing-api", scopeName);

            Assert.Equal(AdminMutationStatus.NotFound, result.Status);
        });
    }

    [Fact]
    public async Task AttachScopeAsync_ScopeAlreadyAttached_IsIdempotentAndReturnsSuccess()
    {
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-api";
        var scopeName = $"{tag}-scope";
        await SeedApiResourceAsync(new ApiResource { Name = name, Enabled = true });
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiScopes.Add(new ApiScope { Name = scopeName, Enabled = true });
            await configDb.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();
            var firstResult = await service.AttachScopeAsync(name, scopeName);
            Assert.True(firstResult.Succeeded);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var result = await service.AttachScopeAsync(name, scopeName);

            Assert.True(result.Succeeded);
            var editor = await service.GetForEditAsync(name);
            Assert.Single(editor!.Scopes);
        });
    }

    [Fact]
    public async Task CreateScopeAsync_ResourceDoesNotExist_ReturnsNotFound()
    {
        var tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var result = await service.CreateScopeAsync(new CreateApiResourceScopeCommand($"{tag}-missing-api", $"{tag}-scope", null));

            Assert.Equal(AdminMutationStatus.NotFound, result.Status);
        });
    }

    [Fact]
    public async Task DetachScopeAsync_ScopeNotAttached_ReturnsNotFound()
    {
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-api";
        await SeedApiResourceAsync(new ApiResource { Name = name, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var result = await service.DetachScopeAsync(name, $"{tag}-never-attached");

            Assert.Equal(AdminMutationStatus.NotFound, result.Status);
        });
    }

    [Fact]
    public async Task AddClaimAsync_ResourceDoesNotExist_ReturnsNotFound()
    {
        var tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var result = await service.AddClaimAsync(new AddApiResourceClaimCommand($"{tag}-missing-api", "department"));

            Assert.Equal(AdminMutationStatus.NotFound, result.Status);
        });
    }

    [Fact]
    public async Task AddClaimAsync_ClaimAlreadyPresent_IsIdempotent()
    {
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-api";
        await SeedApiResourceAsync(new ApiResource { Name = name, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var res1 = await service.AddClaimAsync(new AddApiResourceClaimCommand(name, "department"));
            var res2 = await service.AddClaimAsync(new AddApiResourceClaimCommand(name, "department"));
            Assert.True(res1.Succeeded);
            Assert.True(res2.Succeeded);

            var editor = await service.GetForEditAsync(name);
            Assert.Single(editor!.Claims);
        });
    }

    [Fact]
    public async Task RemoveClaimAsync_ClaimDoesNotExist_ReturnsNotFound()
    {
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-api";
        await SeedApiResourceAsync(new ApiResource { Name = name, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var result = await service.RemoveClaimAsync(name, "never-added");

            Assert.Equal(AdminMutationStatus.NotFound, result.Status);
        });
    }

    [Fact]
    public async Task SetEnabledAsync_ResourceDoesNotExist_ReturnsNotFound()
    {
        var tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var result = await service.SetEnabledAsync($"{tag}-missing-api", enabled: false);

            Assert.Equal(AdminMutationStatus.NotFound, result.Status);
        });
    }

    [Fact]
    public async Task RevokeSecretAsync_ResourceDoesNotExist_ReturnsNotFound()
    {
        var tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var result = await service.RevokeSecretAsync($"{tag}-missing-api", secretId: 1);

            Assert.Equal(AdminMutationStatus.NotFound, result.Status);
        });
    }

    [Fact]
    public async Task AddSecretAsync_ExpirationProvided_PersistsExpiration()
    {
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-api";
        var expiration = DateTime.UtcNow.AddDays(30);
        await SeedApiResourceAsync(new ApiResource { Name = name, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            var result = await service.AddSecretAsync(new AddApiResourceSecretCommand(name, "desc", expiration));
            Assert.True(result.Success);

            var editor = await service.GetForEditAsync(name);
            var secret = Assert.Single(editor!.Secrets);
            Assert.NotNull(secret.Expiration);
        });
    }

    [Fact]
    public async Task CreateScopeAsync_CreatesAndAttachesScope_ThenDetachPreservesTheSystemScope()
    {
        var tag = Guid.NewGuid().ToString("N");
        var resourceName = $"{tag}-api";
        var scopeName = $"{tag}-scope";
        await SeedApiResourceAsync(new ApiResource { Name = resourceName, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();
            var created = await service.CreateScopeAsync(new CreateApiResourceScopeCommand(resourceName, scopeName, "Scope display"));

            Assert.True(created.Succeeded);
            Assert.Contains(scopeName, (await service.GetForEditAsync(resourceName))!.Scopes);
            Assert.Contains(scopeName, await service.GetAllApiScopeNamesAsync());

            var detached = await service.DetachScopeAsync(resourceName, scopeName);

            Assert.True(detached.Succeeded);
            Assert.DoesNotContain(scopeName, (await service.GetForEditAsync(resourceName))!.Scopes);
            Assert.Contains(scopeName, await service.GetAllApiScopeNamesAsync());
        });
    }

    [Fact]
    public async Task SetEnabledAsync_ThenDeleteAsync_PersistsBothSuccessfulMutations()
    {
        var name = $"{Guid.NewGuid():N}-api";
        await SeedApiResourceAsync(new ApiResource { Name = name, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();

            Assert.True((await service.SetEnabledAsync(name, enabled: false)).Succeeded);
            Assert.False((await service.GetForEditAsync(name))!.Enabled);

            Assert.True((await service.DeleteAsync(name)).Succeeded);
            Assert.Null(await service.GetForEditAsync(name));
        });
    }

    [Fact]
    public async Task RevokeSecretAsync_ExistingSecret_RemovesTheSecretWithoutReturningItsValue()
    {
        var name = $"{Guid.NewGuid():N}-api";
        await SeedApiResourceAsync(new ApiResource { Name = name, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();
            var added = await service.AddSecretAsync(new AddApiResourceSecretCommand(name, "temporary", null));
            var secret = Assert.Single((await service.GetForEditAsync(name))!.Secrets);

            Assert.True(added.Success);
            Assert.False(string.IsNullOrWhiteSpace(added.PlaintextSecret));
            Assert.True((await service.RevokeSecretAsync(name, secret.Id)).Succeeded);
            Assert.Empty((await service.GetForEditAsync(name))!.Secrets);
        });
    }
}
