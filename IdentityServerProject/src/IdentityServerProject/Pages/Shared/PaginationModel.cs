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
