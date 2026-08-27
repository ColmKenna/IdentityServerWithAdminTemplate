using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Apis;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Apis;

/// <summary>
/// Integration tests for <see cref="ApiResourceListService"/> exercised against the
/// shared SQLite in-memory database provided by <see cref="AdminWebFactory"/>.
/// </summary>
public class ApiResourceListServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ApiResourceListServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static ApiResource MakeApiResource(string tag, string suffix, bool enabled = true, int scopeCount = 0)
    {
        var resource = new ApiResource
        {
            Name = $"{tag}-api-{suffix}",
            DisplayName = $"{tag} API {suffix}",
            Description = $"Description for {suffix}",
            Enabled = enabled,
            ShowInDiscoveryDocument = true,
            Scopes = new List<ApiResourceScope>()
        };

        for (var i = 1; i <= scopeCount; i++)
        {
            resource.Scopes.Add(new ApiResourceScope
            {
                Scope = $"{tag}-scope-{i}"
            });
        }

        return resource;
    }

    private async Task SeedAsync(params ApiResource[] resources)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            db.ApiResources.AddRange(resources);
            await db.SaveChangesAsync();
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

            var result = await service.GetApiResourcesAsync(new ListQuery($"{tag}-api-alpha", Pagination.From(1, 10)));

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

            var result = await service.GetApiResourcesAsync(new ListQuery($"{tag} API delta", Pagination.From(1, 10)));

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

            var result = await service.GetApiResourcesAsync(new ListQuery($"{tag} API EPSILON".ToUpperInvariant(), Pagination.From(1, 10)));

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

            var result = await service.GetApiResourcesAsync(new ListQuery(tag, Pagination.From(1, 10)));

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

            var result = await service.GetApiResourcesAsync(new ListQuery(tag, Pagination.From(2, 2)));

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

            var result = await service.GetApiResourcesAsync(new ListQuery(tag, Pagination.From(99, 10)));

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

            var result = await service.GetApiResourcesAsync(new ListQuery(tag, Pagination.From(1, 10)));

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

            var result = await service.GetApiResourcesAsync(new ListQuery(tag, Pagination.From(1, 10)));

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

            var result = await service.GetApiResourcesAsync(new ListQuery(tag, Pagination.From(1, 10)));

            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
        });
    }
}
