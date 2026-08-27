namespace IdentityServerProject.Services.Users;

/// <summary>
///     Canonical name of the role the Last-Admin and Self-Demotion guards protect in
///     <see cref="UserDetailsService.RemoveRoleAsync" />. Owned here (rather than by the web host's
///     IdentityServer <c>Config</c>) so the guard has no dependency on host code; the host references
///     this constant instead of defining its own copy.
/// </summary>
public static class ProtectedAdminRoles
{
    public const string SysAdmin = "SysAdmin";
}