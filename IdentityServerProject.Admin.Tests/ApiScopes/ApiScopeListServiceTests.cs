using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Data;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.ApiScopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.ApiScopes;

/// <summary>
/// Exercises <see cref="ApiScopeListService"/> against a real (SQLite in-memory)
/// <see cref="ConfigurationDbContext"/> resolved from the shared <see cref="AdminWebFactory"/> DI container.
/// Every test seeds scopes/clients with a unique tag embedded in the entity names so
/// assertions are unaffected by data left behind by other tests sharing the same connection.
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
        Enabled = enabled,
    };

    private static Client MakeClient(string tag, string suffix, params string[] allowedScopes) => new()
    {
        ClientId = $"{tag}-client-{suffix}",
        ClientName = $"{tag} Client {suffix}",
        AllowedScopes = allowedScopes.Select(s => new ClientScope { Scope = s }).ToList(),
    };

    private async Task SeedAsync(IEnumerable<ApiScope>? scopes = null, IEnumerable<Client>? clients = null)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            if (scopes != null)
            {
                configDb.ApiScopes.AddRange(scopes);
            }
            if (clients != null)
            {
                configDb.Clients.AddRange(clients);
            }
            await configDb.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GetApiScopesAsync_FilterMatchesScopeName_ReturnsOnlyMatchingScope()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(scopes: new[] { MakeApiScope(tag, "alpha"), MakeApiScope(tag, "beta") });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();

            var result = await service.GetApiScopesAsync($"{tag}-scope-alpha", pageNumber: 1, pageSize: 10);

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

            var result = await service.GetApiScopesAsync($"{tag} Scope delta", pageNumber: 1, pageSize: 10);

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

            var result = await service.GetApiScopesAsync($"{tag} SCOPE EPSILON".ToUpperInvariant(), pageNumber: 1, pageSize: 10);

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

            var result = await service.GetApiScopesAsync(filter: tag, pageNumber: 1, pageSize: 10);

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

            var result = await service.GetApiScopesAsync(filter: tag, pageNumber: 2, pageSize: 2);

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

            var result = await service.GetApiScopesAsync(filter: tag, pageNumber: 1, pageSize: 10);

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

            var result = await service.GetApiScopesAsync(filter: tag, pageNumber: 1, pageSize: 10);

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

            var result = await service.GetApiScopesAsync(filter: tag, pageNumber: 1, pageSize: 10);

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

            var result = await service.GetApiScopesAsync(filter: tag, pageNumber: 1, pageSize: 10);

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
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            Assert.False(await configDb.ApiScopes.AnyAsync(s => s.Name == scopeName));
        });
    }

    [Fact]
    public async Task DeleteApiScopeAsync_ScopeReferencedByClient_ReturnsBlockedWithoutMutation()
    {
        var tag = Guid.NewGuid().ToString("N");
        var scopeName = $"{tag}-scope-inuse";
        await SeedAsync(
            scopes: new[] { MakeApiScope(tag, "inuse") },
            clients: new[] { MakeClient(tag, "consumer", scopeName) });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();

            var result = await service.DeleteApiScopeAsync(scopeName);

            Assert.Equal(ApiScopeDeleteResult.Blocked, result);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            Assert.True(await configDb.ApiScopes.AnyAsync(s => s.Name == scopeName));
            
            var client = await configDb.Clients.Include(c => c.AllowedScopes).FirstOrDefaultAsync(c => c.ClientId == $"{tag}-client-consumer");
            Assert.NotNull(client);
            Assert.Contains(client.AllowedScopes, cs => cs.Scope == scopeName);

            var auditDb = sp.GetRequiredService<ApplicationDbContext>();
            var audit = await auditDb.AuditLogEntries.SingleAsync(e =>
                e.Category == AuditCategories.ApiScope && e.Action == AuditActions.Delete && e.TargetId == scopeName);
            Assert.Equal(AuditOutcome.Denied, audit.Outcome);
            Assert.Equal(AuditReasonCodes.ReferencedResource, audit.ReasonCode);
        });
    }

    [Fact]
    public async Task DeleteApiScopeAsync_ScopeDoesNotExist_ReturnsNotFound()
    {
        var tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiScopeListService>();

            var result = await service.DeleteApiScopeAsync($"{tag}-scope-missing");

            Assert.Equal(ApiScopeDeleteResult.NotFound, result);
        });
    }
}


