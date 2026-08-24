using System;
using System.Collections.Generic;
using System.Linq;
using Duende.IdentityServer.EntityFramework.Entities;

namespace IdentityServerProject.Services.IdentityResources;

/// <summary>
/// Decides which identity resources the admin console may mutate, and which of their user claims
/// are structural and may never be removed.
/// </summary>
/// <remarks>
/// <para>
/// Protection is keyed on the <em>resource</em>, not on a claim literal. The distinction matters:
/// an earlier guard rejected the string <c>"openid"</c> wherever it appeared as a claim type, which
/// blocked a meaningless edit on unrelated resources while leaving the resource actually named
/// <c>openid</c> — the one OIDC cannot function without — fully editable.
/// </para>
/// <para>
/// A resource is protected when its name is protocol-mandatory <em>or</em> its row carries
/// <see cref="IdentityResource.NonEditable"/>. Both halves are load-bearing. The flag alone is not
/// enough, because databases seeded before this policy existed have it set to <c>false</c> on every
/// row and there is no migration that would fix them (see DB-001). The name alone is not enough,
/// because <c>NonEditable</c> is the mechanism a deployment uses to protect resources of its own.
/// </para>
/// <para>
/// Only <c>openid</c> is protected by name. <c>profile</c>, <c>email</c>, and <c>roles</c> are
/// seeded too, but their claim sets are legitimately curated by operators and trimming one breaks
/// no protocol invariant. Protecting them would cost real administrative capability and buy no
/// security. A deployment that disagrees sets <c>NonEditable</c> on those rows, which this policy
/// already honours, or adds them to <see cref="InvariantClaimsByResource"/>.
/// </para>
/// </remarks>
public static class BuiltInIdentityResourcePolicy
{
    /// <summary>
    /// The OIDC identity resource every conforming client requests. Removing, disabling, or
    /// altering it breaks authentication for every client at once.
    /// </summary>
    public const string OpenIdResourceName = "openid";

    /// <summary>
    /// The subject identifier. An <c>openid</c> resource that does not carry it stops identifying
    /// anyone, which is a silent failure rather than a loud one.
    /// </summary>
    public const string SubjectClaimType = "sub";

    /// <summary>
    /// Resource name to the user claims that resource cannot exist without. Keys of this map are
    /// exactly the names protected by identity, so adding an entry protects a resource outright.
    /// </summary>
    private static readonly Dictionary<string, string[]> InvariantClaimsByResource =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [OpenIdResourceName] = new[] { SubjectClaimType },
        };

    /// <summary>
    /// True when a resource of this name is protocol-mandatory, regardless of whether a row for it
    /// exists yet. Used by the create path to stop a protected name being re-registered, and by the
    /// seed to stamp <see cref="IdentityResource.NonEditable"/>.
    /// </summary>
    public static bool IsProtectedName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && InvariantClaimsByResource.ContainsKey(name.Trim());

    /// <summary>
    /// True when <paramref name="entity"/> may not be mutated through the admin console — either
    /// because of its name or because its row is flagged non-editable.
    /// </summary>
    public static bool IsProtected(IdentityResource entity) =>
        IsProtectedName(entity.Name) || entity.NonEditable;

    /// <summary>
    /// True when <paramref name="claimType"/> is structural to <paramref name="resourceName"/> and
    /// may never be removed from it. Independent of <see cref="IsProtected"/> on purpose: the
    /// invariant survives a deployment clearing the protection flag.
    /// </summary>
    public static bool IsInvariantClaim(string? resourceName, string? claimType)
    {
        if (string.IsNullOrWhiteSpace(resourceName) || string.IsNullOrWhiteSpace(claimType))
        {
            return false;
        }

        return InvariantClaimsByResource.TryGetValue(resourceName.Trim(), out var invariants)
            && invariants.Contains(claimType.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Operator-facing explanation for a rejected mutation on a protected resource.
    /// </summary>
    public static string ProtectedMessage(string resourceName) =>
        $"'{resourceName}' is a protected identity resource and cannot be modified. " +
        "It is required by the OpenID Connect protocol, or has been marked non-editable.";

    /// <summary>
    /// Operator-facing explanation for a rejected attempt to remove a structural claim.
    /// </summary>
    public static string InvariantClaimMessage(string resourceName, string claimType) =>
        $"The '{claimType}' claim is required by the '{resourceName}' identity resource and cannot be removed.";
}
