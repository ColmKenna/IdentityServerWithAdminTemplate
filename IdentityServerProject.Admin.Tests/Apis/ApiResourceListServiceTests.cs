using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.Apis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Apis;

/// <summary>
/// Exercises <see cref="ApiResourceListService"/> against a real (SQLite in-memory)
/// <see cref="ConfigurationDbContext"/> resolved from the shared <see cref="AdminWebFactory"/> DI container.
/// Every test seeds API resources with a unique tag embedded in the resource name so
/// assertions are unaffected by data left behind by other tests sharing the same connection.
/// </summary>
public class ApiResourceListServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ApiResourceListServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static ApiResource MakeApiResource(string tag, string suffix, bool enabled = true, int scopeCount = 1)
    {
        var resource = new ApiResource
        {
            Name = $"{tag}-api-{suffix}",
            DisplayName = $"{tag} API {suffix}",
            Enabled = enabled,
            Scopes = new List<ApiResourceScope>()
        };

        for (var i = 1; i <= scopeCount; i++)
        {
            resource.Scopes.Add(new ApiResourceScope { Scope = $"{tag}-scope-{i}" });
        }

        return resource;
    }

    private async Task SeedAsync(params ApiResource[] resources)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            foreach (var resource in resources)
            {
                configDb.ApiResources.Add(resource);
            }
            await configDb.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GetApiResourcesAsync_FilterMatchesResourceName_ReturnsOnlyMatchingResource()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeApiResource(tag, "alpha"), MakeApiResource(tag, "beta"));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceListService>();

            var result = await service.GetApiResourcesAsync($"{tag}-api-alpha", pageNumber: 1, pageSize: 10);

            var item = Assert.Single(result.Items);
            Assert.Equal($"{tag}-api-alpha", item.Name);
        });
    }

    [Fact]
    public async Task GetApiResourcesAsync_FilterMatchesDisplayName_ReturnsOnlyMatchingResource()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeApiResource(tag, "gamma"), MakeApiResource(tag, "delta"));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceListService>();

            var result = await service.GetApiResourcesAsync($"{tag} API delta", pageNumber: 1, pageSize: 10);

            var item = Assert.Single(result.Items);
            Assert.Equal($"{tag}-api-delta", item.Name);
        });
    }

    [Fact]
    public async Task GetApiResourcesAsync_FilterIsCaseInsensitive_ReturnsMatch()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeApiResource(tag, "epsilon"));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceListService>();

            var result = await service.GetApiResourcesAsync($"{tag} API EPSILON".ToUpperInvariant(), pageNumber: 1, pageSize: 10);

            Assert.Single(result.Items);
        });
    }

    [Fact]
    public async Task GetApiResourcesAsync_NoFilter_ReturnsResourcesOrderedByName()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeApiResource(tag, "zeta"), MakeApiResource(tag, "alpha2"));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceListService>();

            var result = await service.GetApiResourcesAsync(filter: tag, pageNumber: 1, pageSize: 10);

            Assert.Equal(2, result.Items.Count);
            Assert.True(string.Compare(result.Items[0].Name, result.Items[1].Name, StringComparison.Ordinal) <= 0);
        });
    }

    [Fact]
    public async Task GetApiResourcesAsync_Pagination_ReturnsCorrectPageAndTotalCount()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(
            MakeApiResource(tag, "1"), MakeApiResource(tag, "2"), MakeApiResource(tag, "3"),
            MakeApiResource(tag, "4"), MakeApiResource(tag, "5"));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceListService>();

            var result = await service.GetApiResourcesAsync(filter: tag, pageNumber: 2, pageSize: 2);

            Assert.Equal(2, result.Items.Count);
            Assert.Equal(5, result.TotalCount);
            Assert.Equal(2, result.PageNumber);
        });
    }

    [Fact]
    public async Task GetApiResourcesAsync_PageNumberBeyondLastPage_ReturnsEmptyItemsWithCorrectTotalCount()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeApiResource(tag, "1"), MakeApiResource(tag, "2"));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceListService>();

            var result = await service.GetApiResourcesAsync(filter: tag, pageNumber: 99, pageSize: 10);

            Assert.Empty(result.Items);
            Assert.Equal(2, result.TotalCount);
            Assert.Equal(99, result.PageNumber);
        });
    }

    [Fact]
    public async Task GetApiResourcesAsync_ResourceDisabled_MapsEnabledFalse()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeApiResource(tag, "off", enabled: false));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceListService>();

            var result = await service.GetApiResourcesAsync(filter: tag, pageNumber: 1, pageSize: 10);

            Assert.False(Assert.Single(result.Items).Enabled);
        });
    }

    [Fact]
    public async Task GetApiResourcesAsync_IncludesScopeCount()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeApiResource(tag, "scopes", scopeCount: 3));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceListService>();

            var result = await service.GetApiResourcesAsync(filter: tag, pageNumber: 1, pageSize: 10);

            Assert.Equal(3, Assert.Single(result.Items).ScopeCount);
        });
    }

    [Fact]
    public async Task GetApiResourcesAsync_NoMatchingResources_ReturnsEmptyResultWithZeroTotalCount()
    {
        var tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceListService>();

            var result = await service.GetApiResourcesAsync(filter: tag, pageNumber: 1, pageSize: 10);

            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
        });
    }
}


