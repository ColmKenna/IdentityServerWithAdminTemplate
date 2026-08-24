using System.Threading;
using System.Threading.Tasks;

namespace IdentityServerProject.Services.Clients;

/// <summary>
/// Retrieves OAuth/OIDC clients registered with IdentityServer for display on the Admin console.
/// </summary>
public interface IClientListService
{
    /// <summary>
    /// Returns a filtered, paged list of clients ordered by client name.
    /// </summary>
    /// <param name="filter">
    /// Optional case-insensitive substring matched against client name or client id.
    /// A null or whitespace-only value returns all clients.
    /// </param>
    /// <param name="pageNumber">1-based page number. Values below 1 are treated as 1.</param>
    /// <param name="pageSize">Maximum number of items to return for the page.</param>
    Task<ListResult<ClientListItem>> GetClientsAsync(
        string? filter,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
}
