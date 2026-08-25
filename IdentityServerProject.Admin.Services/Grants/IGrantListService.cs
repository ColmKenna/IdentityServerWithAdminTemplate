using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Users;

namespace IdentityServerProject.Services.Grants;

/// <summary>
/// Retrieves and revokes persisted grants stored by IdentityServer.
/// </summary>
public interface IGrantListService
{
    /// <summary>
    /// Returns a filtered, paged list of active persisted grants ordered by creation time descending.
    /// </summary>
    /// <param name="subjectId">Optional filter for subject ID.</param>
    /// <param name="clientId">Optional filter for client ID.</param>
    /// <param name="typeFilter">Optional filter for grant type.</param>
    /// <param name="pagination">Pagination settings (page number and page size).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListResult<GrantListItem>> GetGrantsAsync(
        UserId? subjectId,
        ClientId? clientId,
        string? typeFilter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes (deletes) a specific persisted grant by key.
    /// </summary>
    Task<RevokeGrantResult> RevokeGrantAsync(GrantKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes (deletes) all persisted grants for a specified subject ID.</summary>
    Task<int> RevokeGrantsBySubjectAsync(UserId subjectId, CancellationToken cancellationToken = default);
}
