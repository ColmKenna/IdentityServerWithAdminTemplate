using IdentityServerProject.Services.Users;

namespace IdentityServerProject.Services.Grants;

/// <summary>
///     Retrieves and revokes persisted grants stored by IdentityServer.
/// </summary>
public interface IGrantListService
{
    /// <summary>
    ///     Returns a filtered, paged list of active persisted grants ordered by creation time descending.
    /// </summary>
    /// <param name="filter">Optional filter criteria for subject ID, client ID, and grant type.</param>
    /// <param name="pagination">Pagination settings (page number and page size).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListResult<GrantListItem>> GetGrantsAsync(
        GrantFilter? filter = null,
        Pagination pagination = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Revokes (deletes) a specific persisted grant by key.
    /// </summary>
    Task<RevokeGrantResult> RevokeGrantAsync(GrantKey key, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Revokes (deletes) all persisted grants for a specified subject ID.
    /// </summary>
    Task<int> RevokeGrantsBySubjectAsync(UserId subjectId, CancellationToken cancellationToken = default);
}