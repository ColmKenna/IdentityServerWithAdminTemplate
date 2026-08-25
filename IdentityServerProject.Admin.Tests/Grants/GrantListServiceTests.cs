using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Grants;
using IdentityServerProject.Services.Users;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Grants;

public class GrantListServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public GrantListServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static PersistedGrant MakeGrant(string key, string clientId, string subjectId, string type = "user_consent", DateTime? expiration = null)
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
            var grantDb = sp.GetRequiredService<PersistedGrantDbContext>();
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();

            if (grants != null)
            {
                foreach (var grant in grants)
                {
                    grantDb.PersistedGrants.Add(grant);
                }
                await grantDb.SaveChangesAsync();
            }

            if (clients != null)
            {
                foreach (var client in clients)
                {
                    configDb.Clients.Add(client);
                }
                await configDb.SaveChangesAsync();
            }
        });
    }

    [Fact]
    public async Task GetGrantsAsync_ReturnsGrants_WithResolvedClientNames()
    {
        var tag = $"grant-test-{Guid.NewGuid():N}";
        var clientId = $"{tag}-client-id";
        var clientName = $"{tag} Client Display Name";

        var client = new Client
        {
            ClientId = clientId,
            ClientName = clientName
        };

        var grant = MakeGrant($"{tag}-key", clientId, $"{tag}-user-1");

        await SeedAsync(new[] { grant }, new[] { client });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IGrantListService>();
            var result = await service.GetGrantsAsync(
                new GrantFilter(ClientId: ClientId.Create(clientId)),
                pagination: Pagination.From(1, 10));

            var item = Assert.Single(result.Items);
            Assert.Equal(clientName, item.ClientName);
            Assert.Equal(clientId, item.ClientId);
        });
    }

    [Fact]
    public async Task GetGrantsAsync_FiltersBySubjectId_ReturnsMatchingGrants()
    {
        var tag = $"grant-subject-{Guid.NewGuid():N}";
        var g1 = MakeGrant($"{tag}-key-1", $"{tag}-client", $"{tag}-target-user");
        var g2 = MakeGrant($"{tag}-key-2", $"{tag}-client", $"{tag}-other-user");

        await SeedAsync(new[] { g1, g2 });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IGrantListService>();
            var result = await service.GetGrantsAsync(
                new GrantFilter(SubjectId: UserId.Create($"{tag}-target-user")),
                pagination: Pagination.From(1, 10));

            var item = Assert.Single(result.Items);
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

        var formatted = GrantListService.FormatRelativeExpiration(expiration, now);

        Assert.Equal(expectedFormat, formatted);
    }

    [Fact]
    public async Task RevokeGrantAsync_ExistingGrant_RemovesGrantFromDatabase()
    {
        var tag = $"grant-revoke-{Guid.NewGuid():N}";
        var grantKey = $"{tag}-key";
        var grant = MakeGrant(grantKey, $"{tag}-client", $"{tag}-user");

        await SeedAsync(new[] { grant });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IGrantListService>();
            var result = await service.RevokeGrantAsync(grantKey);

            Assert.Equal(RevokeGrantResult.Revoked, result);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var grantDb = sp.GetRequiredService<PersistedGrantDbContext>();
            var existing = grantDb.PersistedGrants.FirstOrDefault(g => g.Key == grantKey);
            Assert.Null(existing);
        });
    }

    [Fact]
    public async Task RevokeGrantsBySubjectAsync_RemovesAllSubjectGrants()
    {
        var tag = $"grant-revoke-subject-{Guid.NewGuid():N}";
        var subjectId = $"{tag}-user-multi";
        var g1 = MakeGrant($"{tag}-key-1", $"{tag}-client-1", subjectId);
        var g2 = MakeGrant($"{tag}-key-2", $"{tag}-client-2", subjectId);

        await SeedAsync(new[] { g1, g2 });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IGrantListService>();
            var count = await service.RevokeGrantsBySubjectAsync(subjectId);

            Assert.Equal(2, count);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            var grantDb = sp.GetRequiredService<PersistedGrantDbContext>();
            var existing = grantDb.PersistedGrants.Where(g => g.SubjectId == subjectId).ToList();
            Assert.Empty(existing);
        });
    }
}
