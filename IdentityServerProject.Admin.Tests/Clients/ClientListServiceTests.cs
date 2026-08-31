using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Clients;

/// <summary>
///     Integration tests for <see cref="ClientListService" /> exercised against the
///     shared SQLite in-memory database provided by <see cref="AdminWebFactory" />.
/// </summary>
public class ClientListServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ClientListServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private static Client MakeClient(string tag, string suffix, bool enabled = true,
        string grantType = "authorization_code") => new()
    {
        ClientId = $"{tag}-client-{suffix}",
        ClientName = $"{tag} Client {suffix}",
        Description = $"Description for {suffix}",
        Enabled = enabled,
        ProtocolType = "oidc",
        AllowedGrantTypes = new List<ClientGrantType>
        {
            new() { GrantType = grantType }
        }
    };

    private async Task SeedAsync(params Client[] clients)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.Clients.AddRange(clients);
            await configDb.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GetClientsAsync_FilterMatchesClientName_ReturnsOnlyMatchingClient()
    {
        string tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(tag, "alpha"), MakeClient(tag, "beta"));

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientListService service = sp.GetRequiredService<IClientListService>();

            ListResult<ClientListItem> result =
                await service.GetClientsAsync(new ListQuery($"{tag} Client alpha", Pagination.From(1, 10)));

            ClientListItem item = Assert.Single(result.Items);
            Assert.Equal($"{tag}-client-alpha", item.ClientId);
        });
    }

    [Fact]
    public async Task GetClientsAsync_FilterMatchesClientId_ReturnsOnlyMatchingClient()
    {
        string tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(tag, "gamma"), MakeClient(tag, "delta"));

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientListService service = sp.GetRequiredService<IClientListService>();

            ListResult<ClientListItem> result =
                await service.GetClientsAsync(new ListQuery($"{tag}-client-delta", Pagination.From(1, 10)));

            ClientListItem item = Assert.Single(result.Items);
            Assert.Equal($"{tag}-client-delta", item.ClientId);
        });
    }

    [Fact]
    public async Task GetClientsAsync_FilterIsCaseInsensitive_ReturnsMatch()
    {
        string tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(tag, "epsilon"));

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientListService service = sp.GetRequiredService<IClientListService>();

            ListResult<ClientListItem> result =
                await service.GetClientsAsync(new ListQuery($"{tag} CLIENT EPSILON".ToUpperInvariant(),
                    Pagination.From(1, 10)));

            Assert.Single(result.Items);
        });
    }

    [Fact]
    public async Task GetClientsAsync_NoFilter_ReturnsClientsOrderedByName()
    {
        string tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(tag, "zeta"), MakeClient(tag, "alpha2"));

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientListService service = sp.GetRequiredService<IClientListService>();

            ListResult<ClientListItem> result =
                await service.GetClientsAsync(new ListQuery(tag, Pagination.From(1, 10)));

            Assert.Equal(2, result.Items.Count);
            Assert.True(
                string.Compare(result.Items[0].ClientName, result.Items[1].ClientName, StringComparison.Ordinal) <= 0);
        });
    }

    [Fact]
    public async Task GetClientsAsync_Pagination_ReturnsCorrectPageAndTotalCount()
    {
        string tag = Guid.NewGuid().ToString("N");
        await SeedAsync(
            MakeClient(tag, "1"), MakeClient(tag, "2"), MakeClient(tag, "3"),
            MakeClient(tag, "4"), MakeClient(tag, "5"));

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientListService service = sp.GetRequiredService<IClientListService>();

            ListResult<ClientListItem>
                result = await service.GetClientsAsync(new ListQuery(tag, Pagination.From(2, 2)));

            Assert.Equal(2, result.Items.Count);
            Assert.Equal(5, result.TotalCount);
            Assert.Equal(2, result.PageNumber);
        });
    }

    [Fact]
    public async Task GetClientsAsync_ClientDisabled_MapsEnabledFalse()
    {
        string tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(tag, "off", false));

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientListService service = sp.GetRequiredService<IClientListService>();

            ListResult<ClientListItem> result =
                await service.GetClientsAsync(new ListQuery(tag, Pagination.From(1, 10)));

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
        string tag = Guid.NewGuid().ToString("N");
        await SeedAsync(MakeClient(tag, "type", grantType: grantType));

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientListService service = sp.GetRequiredService<IClientListService>();

            ListResult<ClientListItem> result =
                await service.GetClientsAsync(new ListQuery(tag, Pagination.From(1, 10)));

            Assert.Equal(expectedType, Assert.Single(result.Items).ClientType);
        });
    }

    [Fact]
    public async Task Should_SelectFirstInsertedGrantType_When_ClientHasMultipleGrantTypes()
    {
        var tag = Guid.NewGuid().ToString("N");
        var client = MakeClient(tag, "multiple-grants");
        client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "client_credentials" });
        await SeedAsync(client);

        _factory.ConfigurationCommands.Reset();

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientListService>();

            var result = await service.GetClientsAsync(new ListQuery(tag, Pagination.From(1, 10)));

            Assert.Equal("Authorization Code", Assert.Single(result.Items).ClientType);
        });

        Assert.Equal(2, _factory.ConfigurationCommands.ReadCount);
    }

    [Fact]
    public async Task GetClientsAsync_NoMatchingClients_ReturnsEmptyResultWithZeroTotalCount()
    {
        string tag = Guid.NewGuid().ToString("N");

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientListService service = sp.GetRequiredService<IClientListService>();

            ListResult<ClientListItem> result =
                await service.GetClientsAsync(new ListQuery(tag, Pagination.From(1, 10)));

            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
        });
    }
}