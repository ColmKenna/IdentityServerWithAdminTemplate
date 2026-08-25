using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        string? filter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default)
    {
        pagination = pagination.Normalize();

        var query = ApplyFilter(_configurationDbContext.ApiResources.AsNoTracking(), filter);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(r => r.Name)
            .ThenBy(r => r.DisplayName)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(r => new ApiResourceListItem
            {
                Name = r.Name,
                DisplayName = r.DisplayName,
                ScopeCount = r.Scopes.Count,
                Enabled = r.Enabled,
            })
            .ToListAsync(cancellationToken);

        return new ListResult<ApiResourceListItem>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize,
        };
    }

    private static IQueryable<ApiResource> ApplyFilter(IQueryable<ApiResource> query, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return query;
        }

        var escaped = LikeExtensions.EscapeLikePattern(filter.Trim());
        var pattern = $"%{escaped}%";

        return query.Where(r =>
            EF.Functions.Like(r.Name, pattern) ||
            (r.DisplayName != null && EF.Functions.Like(r.DisplayName, pattern)));
    }
}
