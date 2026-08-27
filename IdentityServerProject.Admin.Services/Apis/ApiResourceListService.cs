using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.Apis;

public class ApiResourceListService : IApiResourceListService
{
    private readonly ConfigurationDbContext _configurationDbContext;

    public ApiResourceListService(ConfigurationDbContext configurationDbContext)
    {
        _configurationDbContext = configurationDbContext;
    }

    public async Task<ListResult<ApiResourceListItem>> GetApiResourcesAsync(
        ListQuery query,
        CancellationToken cancellationToken = default)
    {
        Pagination pagination = query.Pagination.Normalize();

        IQueryable<ApiResource> dbQuery =
            ApplyFilter(_configurationDbContext.ApiResources.AsNoTracking(), query.Filter);

        int totalCount = await dbQuery.CountAsync(cancellationToken);

        List<ApiResourceListItem> items = await dbQuery
            .OrderBy(r => r.Name)
            .ThenBy(r => r.DisplayName)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(r => new ApiResourceListItem
            {
                Name = r.Name,
                DisplayName = r.DisplayName,
                ScopeCount = r.Scopes.Count,
                Enabled = r.Enabled
            })
            .ToListAsync(cancellationToken);

        return new ListResult<ApiResourceListItem>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize
        };
    }

    private static IQueryable<ApiResource> ApplyFilter(IQueryable<ApiResource> query, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return query;

        string? escaped = LikeExtensions.EscapeLikePattern(filter.Trim());
        string pattern = $"%{escaped}%";

        return query.Where(r =>
            EF.Functions.Like(r.Name, pattern) ||
            (r.DisplayName != null && EF.Functions.Like(r.DisplayName, pattern)));
    }
}