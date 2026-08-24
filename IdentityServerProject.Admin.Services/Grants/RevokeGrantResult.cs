namespace IdentityServerProject.Services.Grants;

/// <summary>
/// Result of an attempt to revoke a persisted grant.
/// </summary>
public enum RevokeGrantResult
{
    /// <summary>
    /// Revocation succeeded.
    /// </summary>
    Revoked,

    /// <summary>
    /// The specified grant key was not found.
    /// </summary>
    NotFound,
}
