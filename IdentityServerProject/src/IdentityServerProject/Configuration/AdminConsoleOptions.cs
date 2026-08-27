using IdentityServerProject.Services;

namespace IdentityServerProject.Configuration;

public class AdminConsoleOptions
{
    public const int MinPageSize = Pagination.MinPageSize;
    public const int MaxPageSize = Pagination.MaxPageSize;

    public int DefaultPageSize { get; set; } = 10;
}