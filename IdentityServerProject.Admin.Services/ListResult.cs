using System;
using System.Collections.Generic;

namespace IdentityServerProject.Services;

/// <summary>
/// A generic single page of <typeparamref name="T"/> results, along with enough
/// information for the caller to render pagination controls.
/// </summary>
public class ListResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    public required int TotalCount { get; init; }

    public required int PageNumber { get; init; }

    public required int PageSize { get; init; }

    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public static ListResult<T> Empty(int pageNumber, int pageSize) => new()
    {
        Items = Array.Empty<T>(),
        TotalCount = 0,
        PageNumber = pageNumber,
        PageSize = pageSize,
    };
}
