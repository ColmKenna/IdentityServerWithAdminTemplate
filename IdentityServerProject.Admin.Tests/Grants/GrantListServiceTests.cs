using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Grants;
using IdentityServerProject.Services.Users;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Grants;

public class GrantListServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public GrantListServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static PersistedGrant MakeGrant(string key, string clientId, string subjectId, string type = "user_consent",
        DateTime? expiration = null)
    {
        return new PersistedGrant
        {
            Key = key,
            Type = type,
            ClientId = clientId,
            SubjectId = subjectId,
            CreationTime = DateTime.UtcNow.AddHours(-1),
            Expiration = expiration ?? DateTime.UtcNow.AddDays(7),
            Data = "{}"
        };
    }

    private async Task SeedAsync(IEnumerable<PersistedGrant>? grants = null, IEnumerable<Client>? clients = null)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            PersistedGrantDbContext grantDb = sp.GetRequiredService<PersistedGrantDbContext>();
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();

            if (grants != null)
            {
                foreach (PersistedGrant grant in grants) grantDb.PersistedGrants.Add(grant);
                await grantDb.SaveChangesAsync();
            }

            if (clients != null)
            {
                foreach (Client client in clients) configDb.Clients.Add(client);
                await configDb.SaveChangesAsync();
            }
        });
    }

    [Fact]
    public async Task GetGrantsAsync_ReturnsGrants_WithResolvedClientNames()
    {
        string tag = $"grant-test-{Guid.NewGuid():N}";
        string clientId = $"{tag}-client-id";
        string clientName = $"{tag} Client Display Name";

        var client = new Client
        {
            ClientId = clientId,
            ClientName = clientName
        };

        PersistedGrant grant = MakeGrant($"{tag}-key", clientId, $"{tag}-user-1");

        await SeedAsync(new[] { grant }, new[] { client });

        await _factory.RunInScopeAsync(async sp =>
        {
            IGrantListService service = sp.GetRequiredService<IGrantListService>();
            ListResult<GrantListItem> result = await service.GetGrantsAsync(
                new GrantFilter(ClientId: ClientId.Create(clientId)),
                Pagination.From(1, 10));

            GrantListItem item = Assert.Single(result.Items);
            Assert.Equal(clientName, item.ClientName);
            Assert.Equal(clientId, item.ClientId);
        });
    }

    [Fact]
    public async Task GetGrantsAsync_FiltersBySubjectId_ReturnsMatchingGrants()
    {
        string tag = $"grant-subject-{Guid.NewGuid():N}";
        PersistedGrant g1 = MakeGrant($"{tag}-key-1", $"{tag}-client", $"{tag}-target-user");
        PersistedGrant g2 = MakeGrant($"{tag}-key-2", $"{tag}-client", $"{tag}-other-user");

        await SeedAsync(new[] { g1, g2 });

        await _factory.RunInScopeAsync(async sp =>
        {
            IGrantListService service = sp.GetRequiredService<IGrantListService>();
            ListResult<GrantListItem> result = await service.GetGrantsAsync(
                new GrantFilter(UserId.Create($"{tag}-target-user")),
                Pagination.From(1, 10));

            GrantListItem item = Assert.Single(result.Items);
            Assert.Equal(g1.Key, item.Key);
        });
    }

    [Theory]
    [InlineData(null, "Never")]
    [InlineData(-5, "Expired")]
    [InlineData(120, "in 2 hours")]
    [InlineData(2880, "in 2 days")]
    public void FormatRelativeExpiration_CalculatesCorrectRelativeTimes(int? minutesFromNow, string expectedFormat)
    {
        var now = new DateTime(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);
        DateTime? expiration = minutesFromNow.HasValue ? now.AddMinutes(minutesFromNow.Value) : null;

        string formatted = GrantListService.FormatRelativeExpiration(expiration, now);

        Assert.Equal(expectedFormat, formatted);
    }

    [Fact]
    public async Task RevokeGrantAsync_ExistingGrant_RemovesGrantFromDatabase()
    {
        string tag = $"grant-revoke-{Guid.NewGuid():N}";
        string grantKey = $"{tag}-key";
        PersistedGrant grant = MakeGrant(grantKey, $"{tag}-client", $"{tag}-user");

        await SeedAsync(new[] { grant });

        await _factory.RunInScopeAsync(async sp =>
        {
            IGrantListService service = sp.GetRequiredService<IGrantListService>();
            RevokeGrantResult result = await service.RevokeGrantAsync(GrantKey.Create(grantKey));

            Assert.Equal(RevokeGrantResult.Revoked, result);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            PersistedGrantDbContext grantDb = sp.GetRequiredService<PersistedGrantDbContext>();
            PersistedGrant? existing = grantDb.PersistedGrants.FirstOrDefault(g => g.Key == grantKey);
            Assert.Null(existing);
        });
    }

    [Fact]
    public async Task RevokeGrantsBySubjectAsync_RemovesAllSubjectGrants()
    {
        string tag = $"grant-revoke-subject-{Guid.NewGuid():N}";
        string subjectId = $"{tag}-user-multi";
        PersistedGrant g1 = MakeGrant($"{tag}-key-1", $"{tag}-client-1", subjectId);
        PersistedGrant g2 = MakeGrant($"{tag}-key-2", $"{tag}-client-2", subjectId);

        await SeedAsync(new[] { g1, g2 });

        await _factory.RunInScopeAsync(async sp =>
        {
            IGrantListService service = sp.GetRequiredService<IGrantListService>();
            int count = await service.RevokeGrantsBySubjectAsync(UserId.Create(subjectId));

            Assert.Equal(2, count);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            PersistedGrantDbContext grantDb = sp.GetRequiredService<PersistedGrantDbContext>();
            var existing = grantDb.PersistedGrants.Where(g => g.SubjectId == subjectId).ToList();
            Assert.Empty(existing);
        });
    }
}