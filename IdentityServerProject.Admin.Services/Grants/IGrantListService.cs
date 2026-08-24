using System.Threading;
using System.Threading.Tasks;

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
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="pageSize">Maximum items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListResult<GrantListItem>> GetGrantsAsync(
        string? subjectId,
        string? clientId,
        string? typeFilter,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes (deletes) a specific persisted grant by key.
    /// </summary>
    Task<RevokeGrantResult> RevokeGrantAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes (deletes) all persisted grants for a specified subject ID.
    /// </summary>
    Task<int> RevokeGrantsBySubjectAsync(string subjectId, CancellationToken cancellationToken = default);
}
