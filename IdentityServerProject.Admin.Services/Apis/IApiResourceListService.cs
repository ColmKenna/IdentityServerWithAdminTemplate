using System.Threading;
using System.Threading.Tasks;

namespace IdentityServerProject.Services.Apis;

/// <summary>
/// Retrieves API resources registered with IdentityServer for display on the Admin console.
/// </summary>
public interface IApiResourceListService
{
    /// <summary>
    /// Returns a filtered, paged list of API resources ordered by resource name.
    /// </summary>
    /// <param name="filter">
    /// Optional case-insensitive substring matched against resource name or display name.
    /// A null or whitespace-only value returns all API resources.
    /// </param>
    /// <param name="pageNumber">1-based page number. Values below 1 are treated as 1.</param>
    /// <param name="pageSize">Maximum number of items to return for the page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListResult<ApiResourceListItem>> GetApiResourcesAsync(
        string? filter,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
}
