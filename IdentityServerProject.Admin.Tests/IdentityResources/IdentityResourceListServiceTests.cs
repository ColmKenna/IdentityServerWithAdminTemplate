using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Data;
using IdentityServerProject.Services;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.IdentityResources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.IdentityResources;

public class IdentityResourceListServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public IdentityResourceListServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static IdentityResource MakeIdentityResource(string tag, string suffix) => new()
    {
        Name = $"{tag}-idres-{suffix}",
        DisplayName = $"{tag} IdentityResource {suffix}",
        Description = $"Description for {suffix}",
        Enabled = true,
        Required = true,
        Emphasize = false,
        ShowInDiscoveryDocument = true,
        UserClaims = new List<IdentityResourceClaim>
        {
            new IdentityResourceClaim { Type = "sub" }
        }
    };

    private async Task SeedAsync(IEnumerable<IdentityResource>? resources = null, IEnumerable<Client>? clients = null)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            if (resources != null)
            {
                foreach (var res in resources)
                {
                    configDb.IdentityResources.Add(res);
                }
            }
            if (clients != null)
            {
                foreach (var client in clients)
                {
                    configDb.Clients.Add(client);
                }
            }
            await configDb.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GetIdentityResourcesAsync_FilterMatchesName_ReturnsMatchingResource()
    {
        var tag = $"idres-test-filter-{Guid.NewGuid():N}";
        var r1 = MakeIdentityResource(tag, "alpha");
        var r2 = MakeIdentityResource(tag, "beta");
        await SeedAsync(new[] { r1, r2 });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceListService>();
            var result = await service.GetIdentityResourcesAsync(new ListQuery(tag, Pagination.From(1, 10)));

            Assert.Equal(2, result.TotalCount);
            Assert.Contains(result.Items, item => item.Name == r1.Name);
            Assert.Contains(result.Items, item => item.Name == r2.Name);
        });
    }

    [Fact]
    public async Task GetIdentityResourcesAsync_CountsClientsWithMatchingAllowedScope()
    {
        var tag = $"idres-test-counts-{Guid.NewGuid():N}";
        var r1 = MakeIdentityResource(tag, "referenced");
        var client = new Client
        {
            ClientId = $"{tag}-client",
            ClientName = "Test Client",
            AllowedScopes = new List<ClientScope>
            {
                new ClientScope { Scope = r1.Name }
            }
        };

        await SeedAsync(new[] { r1 }, new[] { client });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceListService>();
            var result = await service.GetIdentityResourcesAsync(new ListQuery(tag, Pagination.From(1, 10)));

            var item = Assert.Single(result.Items);
            Assert.Equal(1, item.ClientReferenceCount);
        });
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_UnreferencedResource_DeletesSuccessfully()
    {
        var tag = $"idres-test-delete-{Guid.NewGuid():N}";
        var resource = MakeIdentityResource(tag, "custom");
        await SeedAsync(new[] { resource });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceListService>();
            var result = await service.DeleteIdentityResourceAsync(resource.Name);

            Assert.Equal(IdentityResourceDeleteResult.Deleted, result);
        });
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_ReferencedResource_ReturnsBlockedWithoutMutation()
    {
        var tag = $"idres-test-blocked-{Guid.NewGuid():N}";
        var resource = MakeIdentityResource(tag, "active");
        var client = new Client
        {
            ClientId = $"{tag}-client",
            ClientName = "Test Client",
            AllowedScopes = new List<ClientScope>
            {
                new ClientScope { Scope = resource.Name }
            }
        };

        await SeedAsync(new[] { resource }, new[] { client });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceListService>();
            var result = await service.DeleteIdentityResourceAsync(resource.Name);

            Assert.Equal(IdentityResourceDeleteResult.Blocked, result);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            Assert.True(await configDb.IdentityResources.AnyAsync(r => r.Name == resource.Name));
            
            var c = await configDb.Clients.Include(cl => cl.AllowedScopes).FirstOrDefaultAsync(cl => cl.ClientId == $"{tag}-client");
            Assert.NotNull(c);
            Assert.Contains(c.AllowedScopes, cs => cs.Scope == resource.Name);

            var auditDb = sp.GetRequiredService<ApplicationDbContext>();
            var audit = await auditDb.AuditLogEntries.SingleAsync(e =>
                e.Category == AuditCategory.IdentityResource && e.Action == AuditAction.Delete && e.TargetId == resource.Name);
            Assert.Equal(AuditOutcome.Denied, audit.Outcome);
            Assert.Equal(AuditReasonCode.ReferencedResource, audit.ReasonCode);
        });
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_OpenIdResource_ReturnsBlocked()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IIdentityResourceListService>();
            var result = await service.DeleteIdentityResourceAsync("openid");

            Assert.Equal(IdentityResourceDeleteResult.Blocked, result);
        });
    }
}
