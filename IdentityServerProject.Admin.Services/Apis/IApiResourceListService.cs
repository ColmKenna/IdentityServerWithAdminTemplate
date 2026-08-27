namespace IdentityServerProject.Services.Apis;

/// <summary>
///     Retrieves API resources registered with IdentityServer for display on the Admin console.
/// </summary>
public interface IApiResourceListService
{
    /// <summary>
    ///     Returns a filtered, paged list of API resources ordered by resource name.
    /// </summary>
    /// <param name="filter">
    ///     Optional case-insensitive substring matched against resource name or display name.
    ///     A null or whitespace-only value returns all API resources.
    /// </param>
    /// <param name="pagination">Pagination settings (page number and page size).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListResult<ApiResourceListItem>> GetApiResourcesAsync(
        ListQuery query,
        CancellationToken cancellationToken = default);
}