namespace IdentityServerProject.Services.Clients;

/// <summary>
///     Retrieves OAuth/OIDC clients registered with IdentityServer for display on the Admin console.
/// </summary>
public interface IClientListService
{
    /// <summary>
    ///     Returns a filtered, paged list of clients ordered by client name.
    /// </summary>
    /// <param name="filter">
    ///     Optional case-insensitive substring matched against client name or client id.
    ///     A null or whitespace-only value returns all clients.
    /// </param>
    /// <param name="pagination">Pagination settings (page number and page size).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListResult<ClientListItem>> GetClientsAsync(
        ListQuery query,
        CancellationToken cancellationToken = default);
}