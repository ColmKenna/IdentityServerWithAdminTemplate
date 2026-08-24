using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Duende.IdentityServer.Models;
using IdentityServerProject.Services.Clients;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Clients;

/// <summary>
/// Exercises <see cref="ClientListService"/> against a real (SQLite in-memory)
/// <see cref="ConfigurationDbContext"/> resolved from the shared <see cref="AdminWebFactory"/> DI container.
/// Every test seeds clients with a unique tag embedded in the client name/id so
/// assertions are unaffected by data left behind by other tests sharing the same connection.
/// </summary>
public class ClientListServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ClientListServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static Client MakeClient(string tag, string suffix, string grantType = "authorization_code", bool enabled = true)
    {
        return new Client
        {
            ClientId = $"{tag}-client-{suffix}",
            ClientName = $"{tag} Client {suffix}",
            Enabled = enabled,
            AllowedGrantTypes = new[] { grantType },
            RequirePkce = false,
        };
    }

    private async Task SeedAsync(params Client[] clients)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            foreach (var client in clients)
            {
                configDb.Clients.Add(client.ToEntity());
            }
            await configDb.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GetClientsAsync_FilterMatchesClientName_ReturnsOnlyMatchingClient()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(tag, "alpha"), MakeClient(tag, "beta"));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientListService>();

            var result = await service.GetClientsAsync($"{tag} Client alpha", pageNumber: 1, pageSize: 10);

            var item = Assert.Single(result.Items);
            Assert.Equal($"{tag}-client-alpha", item.ClientId);
        });
    }

    [Fact]
    public async Task GetClientsAsync_FilterMatchesClientId_ReturnsOnlyMatchingClient()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(tag, "gamma"), MakeClient(tag, "delta"));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientListService>();

            var result = await service.GetClientsAsync($"{tag}-client-delta", pageNumber: 1, pageSize: 10);

            var item = Assert.Single(result.Items);
            Assert.Equal($"{tag}-client-delta", item.ClientId);
        });
    }

    [Fact]
    public async Task GetClientsAsync_FilterIsCaseInsensitive_ReturnsMatch()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(tag, "epsilon"));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientListService>();

            var result = await service.GetClientsAsync($"{tag} CLIENT EPSILON".ToUpperInvariant(), pageNumber: 1, pageSize: 10);

            Assert.Single(result.Items);
        });
    }

    [Fact]
    public async Task GetClientsAsync_NoFilter_ReturnsClientsOrderedByName()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(tag, "zeta"), MakeClient(tag, "alpha2"));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientListService>();

            var result = await service.GetClientsAsync(filter: tag, pageNumber: 1, pageSize: 10);

            Assert.Equal(2, result.Items.Count);
            Assert.True(string.Compare(result.Items[0].ClientName, result.Items[1].ClientName, StringComparison.Ordinal) <= 0);
        });
    }

    [Fact]
    public async Task GetClientsAsync_Pagination_ReturnsCorrectPageAndTotalCount()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(
            MakeClient(tag, "1"), MakeClient(tag, "2"), MakeClient(tag, "3"),
            MakeClient(tag, "4"), MakeClient(tag, "5"));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientListService>();

            var result = await service.GetClientsAsync(filter: tag, pageNumber: 2, pageSize: 2);

            Assert.Equal(2, result.Items.Count);
            Assert.Equal(5, result.TotalCount);
            Assert.Equal(2, result.PageNumber);
        });
    }

    [Fact]
    public async Task GetClientsAsync_ClientDisabled_MapsEnabledFalse()
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(tag, "off", enabled: false));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientListService>();

            var result = await service.GetClientsAsync(filter: tag, pageNumber: 1, pageSize: 10);

            Assert.False(Assert.Single(result.Items).Enabled);
        });
    }

    [Theory]
    [InlineData("authorization_code", "Authorization Code")]
    [InlineData("client_credentials", "Client Credentials")]
    [InlineData("hybrid", "Hybrid")]
    [InlineData("implicit", "Implicit")]
    public async Task GetClientsAsync_DerivesClientTypeFromGrantType(string grantType, string expectedType)
    {
        var tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(tag, "type", grantType: grantType));

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientListService>();

            var result = await service.GetClientsAsync(filter: tag, pageNumber: 1, pageSize: 10);

            Assert.Equal(expectedType, Assert.Single(result.Items).ClientType);
        });
    }

    [Fact]
    public async Task GetClientsAsync_NoMatchingClients_ReturnsEmptyResultWithZeroTotalCount()
    {
        var tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientListService>();

            var result = await service.GetClientsAsync(filter: tag, pageNumber: 1, pageSize: 10);

            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
        });
    }
}


