namespace IdentityServerProject.Services;

public readonly record struct Pagination(int PageNumber, int PageSize, int Skip)
{
    public const int MinPageSize = 1;
    public const int MaxPageSize = 200;

    public static Pagination Normalize(int pageNumber, int pageSize)
    {
        var normalizedPageSize = Math.Clamp(pageSize, MinPageSize, MaxPageSize);
        var maximumPageNumber = (int)Math.Min(
            int.MaxValue,
            ((long)int.MaxValue / normalizedPageSize) + 1);
        var normalizedPageNumber = Math.Clamp(pageNumber, 1, maximumPageNumber);
        var skip = (normalizedPageNumber - 1) * normalizedPageSize;
        return new Pagination(normalizedPageNumber, normalizedPageSize, skip);
    }

    public static Pagination Normalize(Pagination pagination) =>
        Normalize(pagination.PageNumber, pagination.PageSize);

    public static Pagination From(int pageNumber, int pageSize) =>
        Normalize(pageNumber, pageSize);

    public Pagination Normalize() => Normalize(PageNumber, PageSize);
}
