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

    [Fact]
    public async Task Should_ReturnSameGrantListProjection_When_StoredDataIsLarge()
    {
        var tag = $"grant-projection-{Guid.NewGuid():N}";
        var subjectId = $"{tag}-subject";
        var firstClientId = $"{tag}-client-1";
        var secondClientId = $"{tag}-client-2";
        var now = DateTime.UtcNow;
        var firstExpiration = now.AddDays(2);
        var secondExpiration = now.AddDays(3);

        var first = MakeGrant($"{tag}-key-1", firstClientId, subjectId, "refresh_token", firstExpiration);
        first.CreationTime = now.AddHours(-2);
        first.SessionId = $"{tag}-session-1";
        first.Description = "Older grant";
        first.Data = new string('x', 65_536);

        var second = MakeGrant($"{tag}-key-2", secondClientId, subjectId, "authorization_code", secondExpiration);
        second.CreationTime = now.AddHours(-1);
        second.SessionId = $"{tag}-session-2";
        second.Description = "Newer grant";
        second.Data = new string('y', 65_536);

        await SeedAsync(
            new[] { first, second },
            new[]
            {
                new Client { ClientId = firstClientId, ClientName = "First client" },
                new Client { ClientId = secondClientId, ClientName = "Second client" }
            });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IGrantListService>();
            var result = await service.GetGrantsAsync(
                new GrantFilter(SubjectId: UserId.Create(subjectId)),
                pagination: Pagination.From(1, 10));

            Assert.Equal(2, result.TotalCount);
            Assert.Equal(1, result.PageNumber);
            Assert.Equal(10, result.PageSize);

            var newer = result.Items[0];
            Assert.Equal(second.Key, newer.Key.Value);
            Assert.Equal(second.Type, newer.Type);
            Assert.Equal(second.SubjectId, newer.SubjectId?.Value);
            Assert.Equal(second.SessionId, newer.SessionId);
            Assert.Equal(second.ClientId, newer.ClientId.Value);
            Assert.Equal("Second client", newer.ClientName);
            Assert.Equal(second.Description, newer.Description);
            Assert.Equal(second.CreationTime, newer.CreationTime);
            Assert.Equal(second.Expiration, newer.Expiration);
            Assert.False(newer.IsExpired);
            Assert.False(string.IsNullOrWhiteSpace(newer.ExpirationFormatted));

            var older = result.Items[1];
            Assert.Equal(first.Key, older.Key.Value);
            Assert.Equal(first.Type, older.Type);
            Assert.Equal(first.SubjectId, older.SubjectId?.Value);
            Assert.Equal(first.SessionId, older.SessionId);
            Assert.Equal(first.ClientId, older.ClientId.Value);
            Assert.Equal("First client", older.ClientName);
            Assert.Equal(first.Description, older.Description);
            Assert.Equal(first.CreationTime, older.CreationTime);
            Assert.Equal(first.Expiration, older.Expiration);
            Assert.False(older.IsExpired);
            Assert.False(string.IsNullOrWhiteSpace(older.ExpirationFormatted));
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

        _factory.PersistedGrantCommands.Reset();

        await _factory.RunInScopeAsync(async sp =>
        {
            IGrantListService service = sp.GetRequiredService<IGrantListService>();
            int count = await service.RevokeGrantsBySubjectAsync(UserId.Create(subjectId));

            Assert.Equal(2, count);
        });

        var persistedGrantDeletes = _factory.PersistedGrantCommands.Commands
            .Where(command => command.Contains("DELETE FROM \"PersistedGrants\"", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(persistedGrantDeletes);
        Assert.Contains("WHERE", persistedGrantDeletes[0], StringComparison.OrdinalIgnoreCase);

        await _factory.RunInScopeAsync(async sp =>
        {
            PersistedGrantDbContext grantDb = sp.GetRequiredService<PersistedGrantDbContext>();
            var existing = grantDb.PersistedGrants.Where(g => g.SubjectId == subjectId).ToList();
            Assert.Empty(existing);
        });
    }
}