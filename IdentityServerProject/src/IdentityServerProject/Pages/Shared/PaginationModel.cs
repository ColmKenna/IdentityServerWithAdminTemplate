using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using IdentityServerProject.Services;

namespace IdentityServerProject.Pages.Shared;

public class PaginationModel
{
    public int CurrentCount { get; }
    public int TotalCount { get; }
    public int PageNumber { get; }
    public int TotalPages { get; }
    public bool HasPreviousPage { get; }
    public bool HasNextPage { get; }
    public string Noun { get; }
    public string Page { get; }
    public IDictionary<string, string?> RouteValues { get; }

    public PaginationModel(
        int currentCount,
        int totalCount,
        int pageNumber,
        int totalPages,
        bool hasPreviousPage,
        bool hasNextPage,
        string noun,
        string page = "./Index",
        IDictionary<string, string?>? routeValues = null)
    {
        CurrentCount = currentCount;
        TotalCount = totalCount;
        PageNumber = pageNumber;
        TotalPages = totalPages;
        HasPreviousPage = hasPreviousPage;
        HasNextPage = hasNextPage;
        Noun = noun;
        Page = page;
        RouteValues = routeValues ?? new Dictionary<string, string?>();
    }

    /// <summary>
    ///     Route values for the previous page link, or null when there is no previous page.
    /// </summary>
    public IDictionary<string, string?>? PreviousPageRoute(IQueryCollection? currentQuery) =>
        HasPreviousPage ? PageRoute(PageNumber - 1, currentQuery) : null;

    /// <summary>
    ///     Route values for the next page link, or null when there is no next page.
    /// </summary>
    public IDictionary<string, string?>? NextPageRoute(IQueryCollection? currentQuery) =>
        HasNextPage ? PageRoute(PageNumber + 1, currentQuery) : null;

    private Dictionary<string, string?> PageRoute(int pageNumber, IQueryCollection? currentQuery)
    {
        Dictionary<string, string?> route = ResolveBaseRoute(currentQuery);
        route["PageNumber"] = pageNumber.ToString();
        return route;
    }

    /// <summary>
    ///     What a page link carries besides the page number: the route values the page
    ///     supplied, or, when it supplied none, whatever the current query string holds.
    ///     Empty values are dropped either way, so a blank filter never reaches the link.
    /// </summary>
    private Dictionary<string, string?> ResolveBaseRoute(IQueryCollection? currentQuery)
    {
        var resolved = new Dictionary<string, string?>(RouteValues);

        if (resolved.Count == 0 && currentQuery is not null)
            foreach (KeyValuePair<string, StringValues> entry in currentQuery)
                if (!entry.Key.Equals("PageNumber", StringComparison.OrdinalIgnoreCase))
                    resolved[entry.Key] = entry.Value.ToString();

        return resolved
            .Where(entry => !string.IsNullOrEmpty(entry.Value))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
    }
}

public static class PaginationExtensions
{
    public static PaginationModel ToPagination<T>(
        this ListResult<T> result,
        string noun,
        string? filter = null,
        string page = "./Index")
    {
        var routeValues = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            routeValues["Filter"] = filter;
        }

        return new PaginationModel(
            result.Items.Count,
            result.TotalCount,
            result.PageNumber,
            result.TotalPages,
            result.HasPreviousPage,
            result.HasNextPage,
            noun,
            page,
            routeValues);
    }

    public static PaginationModel ToPagination<T>(
        this ListResult<T> result,
        string noun,
        IDictionary<string, string?> routeValues,
        string page = "./Index")
    {
        return new PaginationModel(
            result.Items.Count,
            result.TotalCount,
            result.PageNumber,
            result.TotalPages,
            result.HasPreviousPage,
            result.HasNextPage,
            noun,
            page,
            routeValues);
    }
}
