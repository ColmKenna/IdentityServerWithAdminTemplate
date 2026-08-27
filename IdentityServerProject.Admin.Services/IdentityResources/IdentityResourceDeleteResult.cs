namespace IdentityServerProject.Services.IdentityResources;

/// <summary>
///     Result of an attempt to delete an Identity Resource.
/// </summary>
public enum IdentityResourceDeleteResult
{
    /// <summary>
    ///     Deletion succeeded.
    /// </summary>
    Deleted,

    /// <summary>
    ///     The specified identity resource was not found.
    /// </summary>
    NotFound,

    /// <summary>
    ///     Deletion was blocked because one or more clients reference this resource or it is a non-editable built-in.
    /// </summary>
    Blocked
}