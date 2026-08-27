namespace IdentityServerProject.Services.IdentityResources;

/// <summary>
/// Retrieves and manages Identity Resources registered with IdentityServer for display on the Admin console.
/// </summary>
public interface IIdentityResourceListService
{
    /// <summary>
    /// Returns a filtered, paged list of Identity Resources ordered by name, including
    /// how many distinct clients currently reference each resource.
    /// </summary>
    /// <param name="filter">
    /// Optional case-insensitive substring matched against resource name or display name.
    /// A null or whitespace-only value returns all identity resources.
    /// </param>
    /// <param name="pagination">Pagination settings (page number and page size).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListResult<IdentityResourceListItem>> GetIdentityResourcesAsync(
        ListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the named Identity Resource, refusing the operation if any client still references it or if it is non-editable.
    /// </summary>
    Task<IdentityResourceDeleteResult> DeleteIdentityResourceAsync(string name, CancellationToken cancellationToken = default);
}
