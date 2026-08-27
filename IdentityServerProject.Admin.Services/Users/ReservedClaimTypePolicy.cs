using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IdentityServerProject.Services.Users;

/// <summary>
/// Decides which claim types an administrator may create through the Users &rarr; Claims editor.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET Identity's <c>UserClaimsPrincipalFactory</c> copies every stored user claim onto the
/// signed-in principal verbatim. A user claim is therefore indistinguishable from a claim the
/// framework issued itself, so a stored claim of the configured role claim type — <c>"role"</c>
/// here, because Duende's <c>AddAspNetIdentity</c> rewrites <see cref="IdentityOptions"/> to the
/// JWT short forms — satisfies <c>RequireRole(...)</c>, and hence the <c>SysAdminOnly</c> policy,
/// without any row in <c>AspNetUserRoles</c>. Such a grant is invisible to the Roles tab, to
/// <c>UserManager.GetRolesAsync</c>, and to the Last-Admin Guard in
/// <see cref="UserDetailsService.RemoveRoleAsync"/>, which counts role holders via
/// <c>GetUsersInRoleAsync</c>. Because <c>Config.cs</c> also publishes a <c>roles</c> identity
/// resource, the same claim can flow into tokens issued to downstream clients.
/// </para>
/// <para>
/// Adding a reserved type is blocked. Removing one is deliberately still allowed: removal only
/// ever de-escalates, and an operator needs a way to clean up claims injected before this policy
/// existed. <see cref="Diagnostics.IDiagnosticsService"/> reports any that already exist.
/// </para>
/// <para>
/// Comparison is ordinal case-insensitive on the trimmed type, matching
/// <c>ClaimsIdentity.HasClaim</c>, which compares claim types with
/// <see cref="StringComparison.OrdinalIgnoreCase"/> — a stored <c>"ROLE"</c> is honoured exactly
/// as a stored <c>"role"</c> would be.
/// </para>
/// </remarks>
public sealed class ReservedClaimTypePolicy
{
    /// <summary>
    /// Prefix Identity uses for its own internal principal claims (security stamp, etc.).
    /// </summary>
    public const string AspNetIdentityPrefix = "AspNet.Identity.";

    /// <summary>
    /// Types reserved regardless of how <see cref="IdentityOptions"/> is configured. The
    /// configured values are added on top in the constructor, so a deployment that overrides
    /// (say) the role claim type has both the default and its override blocked.
    /// </summary>
    private static readonly string[] WellKnownReservedTypes =
    {
        // WS-* URI forms — what ASP.NET Identity authorization reads when nothing overrides it.
        ClaimTypes.Role,
        ClaimTypes.NameIdentifier,

        // OIDC/JWT short forms. Duende's AddAspNetIdentity rewrites IdentityOptions to these, so
        // in this deployment "role" — not ClaimTypes.Role — is the type that actually satisfies
        // RequireRole. Both are blocked so the guard survives that configuration changing.
        "role",
        "roles",
        "sub",
        "amr",
        "idp",
        "auth_time",
    };

    private readonly HashSet<string> _reservedTypes;

    public ReservedClaimTypePolicy(IOptions<IdentityOptions> identityOptions)
    {
        var claimsIdentity = identityOptions.Value.ClaimsIdentity;

        // Only the options that carry an authorization or identity decision. UserNameClaimType and
        // EmailClaimType are deliberately excluded: Duende sets the former to "name", which
        // SeedData writes onto every user as an ordinary profile claim, and blocking display
        // claims buys no security while breaking an established convention.
        _reservedTypes = new HashSet<string>(WellKnownReservedTypes, StringComparer.OrdinalIgnoreCase)
        {
            claimsIdentity.RoleClaimType,
            claimsIdentity.UserIdClaimType,
            claimsIdentity.SecurityStampClaimType,
        };

        ReservedTypes = _reservedTypes.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The reserved types, for display on the Claims tab and in diagnostics. Excludes the
    /// <see cref="AspNetIdentityPrefix"/> rule, which is a prefix match rather than an exact one.
    /// </summary>
    public IReadOnlyList<string> ReservedTypes { get; }

    /// <summary>
    /// True when <paramref name="claimType"/> may not be assigned through the admin claim editor.
    /// </summary>
    public bool IsReserved(string? claimType)
    {
        var normalized = Normalize(claimType);
        if (normalized.Length == 0)
            return false;

        return _reservedTypes.Contains(normalized)
            || normalized.StartsWith(AspNetIdentityPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Trims a submitted claim type. Whitespace is stripped before the reserved check so that
    /// <c>" role "</c> cannot be stored as a distinct-but-equivalent type, and so a stored type
    /// round-trips exactly when the Claims tab posts it back for removal.
    /// </summary>
    public static string Normalize(string? claimType) => claimType?.Trim() ?? string.Empty;
}
