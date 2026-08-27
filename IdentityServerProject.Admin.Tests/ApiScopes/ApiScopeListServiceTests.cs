using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.ApiScopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.ApiScopes;

/// <summary>
/// Integration tests for <see cref="ApiScopeListService"/> exercised against the
/// shared SQLite in-memory database provided by <see cref="AdminWebFactory"/>.
/// </summary>
public class ApiScopeListServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ApiScopeListServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static ApiScope MakeApiScope(string tag, string suffix, bool enabled = true) => new()
    {
        Name = $"{tag}-scope-{suffix}",
        DisplayName = $"{tag} Scope {suffix}",
        Description = $"Description for {suffix}",
        Enabled = enabled,
        Required = false,
        Emphasize = false,
        ShowInDiscoveryDocument = true,
    };

    private static Client MakeClient(string tag, string suffix, params string[] allowedScopes)
    {
        var client = new Client
        {
            ClientId = $"{tag}-client-{suffix}",
            ClientName = $"{tag} Client {suffix}",
            ProtocolType = "oidc",
            Enabled = true,
            AllowedScopes = new List<ClientScope>()
        };

        foreach (var scope in allowedScopes)
        {
            client.AllowedScopes.Add(new ClientScope { Scope = scope });
        }

        return client;
    }

    private async Task SeedAsync(IEnumerable<ApiScope>? scopes = null, IEnumerable<Client>? clients = null)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            if (scopes != null)
            {
                db.ApiScopes.AddRange(scopes);
            }
            if (clients != null)
            {
                db.Clients.AddRange(clients);
            }
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GetApiScopesAsync_FilterMatchesName_ReturnsOnlyMatchingScope()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(scopes: new[] { MakeApiScope(tag, "alpha"), MakeApiScope(tag, "beta") });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();

            var result = await service.GetApiScopesAsync(new ListQuery($"{tag}-scope-alpha", Pagination.From(1, 10)));

            var item = Assert.Single(result.Items);
            Assert.Equal($"{tag}-scope-alpha", item.Name);
        });
    }

    [Fact]
    public async Task GetApiScopesAsync_FilterMatchesDisplayName_ReturnsOnlyMatchingScope()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(scopes: new[] { MakeApiScope(tag, "gamma"), MakeApiScope(tag, "delta") });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();

            var result = await service.GetApiScopesAsync(new ListQuery($"{tag} Scope delta", Pagination.From(1, 10)));

            var item = Assert.Single(result.Items);
            Assert.Equal($"{tag}-scope-delta", item.Name);
        });
    }

    [Fact]
    public async Task GetApiScopesAsync_FilterIsCaseInsensitive_ReturnsMatch()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(scopes: new[] { MakeApiScope(tag, "epsilon") });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();

            var result = await service.GetApiScopesAsync(new ListQuery($"{tag} SCOPE EPSILON".ToUpperInvariant(), Pagination.From(1, 10)));

            Assert.Single(result.Items);
        });
    }

    [Fact]
    public async Task GetApiScopesAsync_NoFilter_ReturnsScopesOrderedByName()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(scopes: new[] { MakeApiScope(tag, "zeta"), MakeApiScope(tag, "alpha2") });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();

            var result = await service.GetApiScopesAsync(new ListQuery(tag, Pagination.From(1, 10)));

            Assert.Equal(2, result.Items.Count);
            Assert.True(string.Compare(result.Items[0].Name, result.Items[1].Name, StringComparison.Ordinal) <= 0);
        });
    }

    [Fact]
    public async Task GetApiScopesAsync_Pagination_ReturnsCorrectPageAndTotalCount()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(scopes: new[]
        {
            MakeApiScope(tag, "1"), MakeApiScope(tag, "2"), MakeApiScope(tag, "3"),
            MakeApiScope(tag, "4"), MakeApiScope(tag, "5"),
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();

            var result = await service.GetApiScopesAsync(new ListQuery(tag, Pagination.From(2, 2)));

            Assert.Equal(2, result.Items.Count);
            Assert.Equal(5, result.TotalCount);
            Assert.Equal(2, result.PageNumber);
        });
    }

    [Fact]
    public async Task GetApiScopesAsync_ScopeDisabled_MapsEnabledFalse()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(scopes: new[] { MakeApiScope(tag, "off", enabled: false) });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();

            var result = await service.GetApiScopesAsync(new ListQuery(tag, Pagination.From(1, 10)));

            Assert.False(Assert.Single(result.Items).Enabled);
        });
    }

    [Fact]
    public async Task GetApiScopesAsync_NoMatchingScopes_ReturnsEmptyResultWithZeroTotalCount()
    {
        var tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();

            var result = await service.GetApiScopesAsync(new ListQuery(tag, Pagination.From(1, 10)));

            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
        });
    }

    [Fact]
    public async Task GetApiScopesAsync_ScopeUnreferenced_ReportsZeroClientReferenceCount()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(scopes: new[] { MakeApiScope(tag, "unused") });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();

            var result = await service.GetApiScopesAsync(new ListQuery(tag, Pagination.From(1, 10)));

            Assert.Equal(0, Assert.Single(result.Items).ClientReferenceCount);
        });
    }

    [Fact]
    public async Task GetApiScopesAsync_ScopeReferencedByMultipleClients_ReportsDistinctClientCount()
    {
        var tag = Guid.NewGuid().ToString("N");
        var scopeName = $"{tag}-scope-shared";
        await SeedAsync(
            scopes: new[] { MakeApiScope(tag, "shared") },
            clients: new[]
            {
                MakeClient(tag, "a", scopeName),
                MakeClient(tag, "b", scopeName),
            });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();

            var result = await service.GetApiScopesAsync(new ListQuery(tag, Pagination.From(1, 10)));

            Assert.Equal(2, Assert.Single(result.Items).ClientReferenceCount);
        });
    }

    [Fact]
    public async Task DeleteApiScopeAsync_ScopeUnreferenced_DeletesAndReturnsDeleted()
    {
        var tag = Guid.NewGuid().ToString("N");
        var scopeName = $"{tag}-scope-deleteme";
        await SeedAsync(scopes: new[] { MakeApiScope(tag, "deleteme") });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();

            var result = await service.DeleteApiScopeAsync(scopeName);

            Assert.Equal(ApiScopeDeleteResult.Deleted, result);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            var exists = await db.ApiScopes.AnyAsync(s => s.Name == scopeName);
            Assert.False(exists);
        });
    }

    [Fact]
    public async Task DeleteApiScopeAsync_ScopeReferencedByClient_ReturnsBlockedAndLeavesScopeIntact()
    {
        var tag = Guid.NewGuid().ToString("N");
        var scopeName = $"{tag}-scope-inuse";
        await SeedAsync(
            scopes: new[] { MakeApiScope(tag, "inuse") },
            clients: new[] { MakeClient(tag, "holder", scopeName) });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();

            var result = await service.DeleteApiScopeAsync(scopeName);

            Assert.Equal(ApiScopeDeleteResult.Blocked, result);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            var exists = await db.ApiScopes.AnyAsync(s => s.Name == scopeName);
            Assert.True(exists);
        });
    }

    [Fact]
    public async Task DeleteApiScopeAsync_ScopeDoesNotExist_ReturnsNotFound()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();

            var result = await service.DeleteApiScopeAsync($"missing-scope-{Guid.NewGuid():N}");

            Assert.Equal(ApiScopeDeleteResult.NotFound, result);
        });
    }
}
