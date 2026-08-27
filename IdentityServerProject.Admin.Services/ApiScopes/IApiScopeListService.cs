namespace IdentityServerProject.Services.ApiScopes;

/// <summary>
///     Retrieves and manages API scopes registered with IdentityServer for display on the Admin console.
/// </summary>
public interface IApiScopeListService
{
    /// <summary>
    ///     Returns a filtered, paged list of API scopes ordered by scope name, including
    ///     how many distinct clients currently reference each scope.
    /// </summary>
    /// <param name="filter">
    ///     Optional case-insensitive substring matched against scope name or display name.
    ///     A null or whitespace-only value returns all API scopes.
    /// </param>
    /// <param name="pagination">Pagination settings (page number and page size).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListResult<ApiScopeListItem>> GetApiScopesAsync(
        ListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes the named API scope, refusing the operation if any client still references it.
    /// </summary>
    Task<ApiScopeDeleteResult> DeleteApiScopeAsync(string name, CancellationToken cancellationToken = default);
}