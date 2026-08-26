using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.Clients;

public class ClientListService : IClientListService
{
    private const string GrantTypeAuthorizationCode = "authorization_code";
    private const string GrantTypeClientCredentials = "client_credentials";
    private const string GrantTypeHybrid = "hybrid";
    private const string GrantTypeImplicit = "implicit";
    private const string GrantTypeDeviceCode = "urn:ietf:params:oauth:grant-type:device_code";

    private readonly ConfigurationDbContext _configurationDbContext;

    public ClientListService(ConfigurationDbContext configurationDbContext)
    {
        _configurationDbContext = configurationDbContext;
    }

    public async Task<ListResult<ClientListItem>> GetClientsAsync(
        ListQuery query,
        CancellationToken cancellationToken = default)
    {
        var pagination = query.Pagination.Normalize();

        var dbQuery = ApplyFilter(_configurationDbContext.Clients.AsNoTracking(), query.Filter);

        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var pageEntities = await dbQuery
            .OrderBy(c => c.ClientName)
            .ThenBy(c => c.ClientId)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Include(c => c.AllowedGrantTypes)
            .ToListAsync(cancellationToken);

        var items = pageEntities.Select(MapToListItem).ToList();

        return new ListResult<ClientListItem>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize,
        };
    }

    private static IQueryable<Client> ApplyFilter(IQueryable<Client> query, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return query;
        }

        var escaped = LikeExtensions.EscapeLikePattern(filter.Trim());
        var pattern = $"%{escaped}%";
        
        return query.Where(c =>
            (c.ClientName != null && EF.Functions.Like(c.ClientName, pattern)) ||
            EF.Functions.Like(c.ClientId, pattern));
    }

    private static ClientListItem MapToListItem(Client entity)
    {
        return new ClientListItem
        {
            ClientId = entity.ClientId,
            ClientName = string.IsNullOrWhiteSpace(entity.ClientName) ? entity.ClientId : entity.ClientName,
            ClientType = DeriveClientType(entity),
            Enabled = entity.Enabled,
        };
    }

    private static string DeriveClientType(Client entity)
    {
        var grantType = entity.AllowedGrantTypes.Select(g => g.GrantType).FirstOrDefault();

        return grantType switch
        {
            null => "Unknown",
            GrantTypeAuthorizationCode => "Authorization Code",
            GrantTypeClientCredentials => "Client Credentials",
            GrantTypeHybrid => "Hybrid",
            GrantTypeImplicit => "Implicit",
            GrantTypeDeviceCode => "Device Flow",
            _ => Prettify(grantType),
        };
    }

    private static string Prettify(string grantType)
    {
        var words = grantType.Replace('_', ' ').Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w)));
    }
}
