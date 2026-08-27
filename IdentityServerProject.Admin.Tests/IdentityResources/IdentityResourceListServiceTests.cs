using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.IdentityResources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
            new() { Type = "sub" }
        }
    };

    private async Task SeedAsync(IEnumerable<IdentityResource>? resources = null, IEnumerable<Client>? clients = null)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            if (resources != null)
                foreach (IdentityResource res in resources)
                    configDb.IdentityResources.Add(res);

            if (clients != null)
                foreach (Client client in clients)
                    configDb.Clients.Add(client);

            await configDb.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GetIdentityResourcesAsync_FilterMatchesName_ReturnsMatchingResource()
    {
        string tag = $"idres-test-filter-{Guid.NewGuid():N}";
        IdentityResource r1 = MakeIdentityResource(tag, "alpha");
        IdentityResource r2 = MakeIdentityResource(tag, "beta");
        await SeedAsync(new[] { r1, r2 });

        await _factory.RunInScopeAsync(async sp =>
        {
            IIdentityResourceListService service = sp.GetRequiredService<IIdentityResourceListService>();
            ListResult<IdentityResourceListItem> result =
                await service.GetIdentityResourcesAsync(new ListQuery(tag, Pagination.From(1, 10)));

            Assert.Equal(2, result.TotalCount);
            Assert.Contains(result.Items, item => item.Name == r1.Name);
            Assert.Contains(result.Items, item => item.Name == r2.Name);
        });
    }

    [Fact]
    public async Task GetIdentityResourcesAsync_CountsClientsWithMatchingAllowedScope()
    {
        string tag = $"idres-test-counts-{Guid.NewGuid():N}";
        IdentityResource r1 = MakeIdentityResource(tag, "referenced");
        var client = new Client
        {
            ClientId = $"{tag}-client",
            ClientName = "Test Client",
            AllowedScopes = new List<ClientScope>
            {
                new() { Scope = r1.Name }
            }
        };

        await SeedAsync(new[] { r1 }, new[] { client });

        await _factory.RunInScopeAsync(async sp =>
        {
            IIdentityResourceListService service = sp.GetRequiredService<IIdentityResourceListService>();
            ListResult<IdentityResourceListItem> result =
                await service.GetIdentityResourcesAsync(new ListQuery(tag, Pagination.From(1, 10)));

            IdentityResourceListItem item = Assert.Single(result.Items);
            Assert.Equal(1, item.ClientReferenceCount);
        });
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_UnreferencedResource_DeletesSuccessfully()
    {
        string tag = $"idres-test-delete-{Guid.NewGuid():N}";
        IdentityResource resource = MakeIdentityResource(tag, "custom");
        await SeedAsync(new[] { resource });

        await _factory.RunInScopeAsync(async sp =>
        {
            IIdentityResourceListService service = sp.GetRequiredService<IIdentityResourceListService>();
            IdentityResourceDeleteResult result = await service.DeleteIdentityResourceAsync(resource.Name);

            Assert.Equal(IdentityResourceDeleteResult.Deleted, result);
        });
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_ReferencedResource_ReturnsBlockedWithoutMutation()
    {
        string tag = $"idres-test-blocked-{Guid.NewGuid():N}";
        IdentityResource resource = MakeIdentityResource(tag, "active");
        var client = new Client
        {
            ClientId = $"{tag}-client",
            ClientName = "Test Client",
            AllowedScopes = new List<ClientScope>
            {
                new() { Scope = resource.Name }
            }
        };

        await SeedAsync(new[] { resource }, new[] { client });

        await _factory.RunInScopeAsync(async sp =>
        {
            IIdentityResourceListService service = sp.GetRequiredService<IIdentityResourceListService>();
            IdentityResourceDeleteResult result = await service.DeleteIdentityResourceAsync(resource.Name);

            Assert.Equal(IdentityResourceDeleteResult.Blocked, result);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            Assert.True(await configDb.IdentityResources.AnyAsync(r => r.Name == resource.Name));

            Client? c = await configDb.Clients.Include(cl => cl.AllowedScopes)
                .FirstOrDefaultAsync(cl => cl.ClientId == $"{tag}-client");
            Assert.NotNull(c);
            Assert.Contains(c.AllowedScopes, cs => cs.Scope == resource.Name);

            ApplicationDbContext auditDb = sp.GetRequiredService<ApplicationDbContext>();
            AuditLogEntry audit = await auditDb.AuditLogEntries.SingleAsync(e =>
                e.Category == AuditCategory.IdentityResource && e.Action == AuditAction.Delete &&
                e.TargetId == resource.Name);
            Assert.Equal(AuditOutcome.Denied, audit.Outcome);
            Assert.Equal(AuditReasonCode.ReferencedResource, audit.ReasonCode);
        });
    }

    [Fact]
    public async Task DeleteIdentityResourceAsync_OpenIdResource_ReturnsBlocked()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IIdentityResourceListService service = sp.GetRequiredService<IIdentityResourceListService>();
            IdentityResourceDeleteResult result = await service.DeleteIdentityResourceAsync("openid");

            Assert.Equal(IdentityResourceDeleteResult.Blocked, result);
        });
    }
}