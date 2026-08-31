using System.Globalization;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.Clients;

public class ClientListService(ConfigurationDbContext configurationDbContext) : IClientListService
{
    private const string GrantTypeAuthorizationCode = "authorization_code";
    private const string GrantTypeClientCredentials = "client_credentials";
    private const string GrantTypeHybrid = "hybrid";
    private const string GrantTypeImplicit = "implicit";
    private const string GrantTypeDeviceCode = "urn:ietf:params:oauth:grant-type:device_code";

    private readonly ConfigurationDbContext _configurationDbContext = configurationDbContext;

    public async Task<ListResult<ClientListItem>> GetClientsAsync(
        ListQuery query,
        CancellationToken cancellationToken = default)
    {
        Pagination pagination = query.Pagination.Normalize();

        IQueryable<Client> dbQuery = ApplyFilter(_configurationDbContext.Clients.AsNoTracking(), query.Filter);

        int totalCount = await dbQuery.CountAsync(cancellationToken);

        List<ClientListRow> rows = await dbQuery
            .OrderBy(c => c.ClientName)
            .ThenBy(c => c.ClientId)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(c => new ClientListRow(
                c.ClientId,
                c.ClientName,
                c.Enabled,
                c.AllowedGrantTypes.OrderBy(g => g.Id).Select(g => g.GrantType).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var items = rows.Select(MapToListItem).ToList();

        return new ListResult<ClientListItem>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize
        };
    }

    private sealed record ClientListRow(
        string ClientId,
        string? ClientName,
        bool Enabled,
        string? PrimaryGrantType);

    private static IQueryable<Client> ApplyFilter(IQueryable<Client> query, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return query;

        string? escaped = LikeExtensions.EscapeLikePattern(filter.Trim());
        string pattern = $"%{escaped}%";

        return query.Where(c =>
            (c.ClientName != null && EF.Functions.Like(c.ClientName, pattern)) ||
            EF.Functions.Like(c.ClientId, pattern));
    }

    private static ClientListItem MapToListItem(ClientListRow row)
    {
        return new ClientListItem
        {
            ClientId = row.ClientId,
            ClientName = string.IsNullOrWhiteSpace(row.ClientName) ? row.ClientId : row.ClientName,
            ClientType = DeriveClientType(row.PrimaryGrantType),
            Enabled = row.Enabled
        };
    }

    private static string DeriveClientType(string? grantType)
    {
        return grantType switch
        {
            null => "Unknown",
            GrantTypeAuthorizationCode => "Authorization Code",
            GrantTypeClientCredentials => "Client Credentials",
            GrantTypeHybrid => "Hybrid",
            GrantTypeImplicit => "Implicit",
            GrantTypeDeviceCode => "Device Flow",
            _ => Prettify(grantType)
        };
    }

    private static string Prettify(string grantType)
    {
        string[] words = grantType.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w)));
    }
}