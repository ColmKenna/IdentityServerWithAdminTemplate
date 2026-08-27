namespace IdentityServerProject.Services.ApiScopes;

/// <summary>
///     Outcome of an attempt to delete an API scope.
/// </summary>
public enum ApiScopeDeleteResult
{
    Deleted,
    NotFound,

    /// <summary>
    ///     The scope is still referenced by at least one client's AllowedScopes and was not deleted.
    /// </summary>
    Blocked
}