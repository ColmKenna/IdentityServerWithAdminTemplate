using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.IdentityResources;

public sealed record CreateIdentityResourceCommand(
    ScopeName Name,
    string? DisplayName,
    string? Description,
    bool Enabled,
    bool Required,
    bool Emphasize,
    bool ShowInDiscoveryDocument,
    List<string> UserClaims);

public sealed record UpdateIdentityResourceBasicsCommand(
    ScopeName Name,
    string? DisplayName,
    string? Description,
    bool Enabled,
    bool Required,
    bool Emphasize,
    bool ShowInDiscoveryDocument);

/// <summary>
///     Reads and mutates a single identity resource for the Admin &gt; Identity Resources edit page.
///     The resource's Name is immutable through this service — there is no rename operation.
/// </summary>
/// <remarks>
///     Every mutation is refused for a protected resource, uniformly across all three methods — see
///     <see cref="BuiltInIdentityResourcePolicy" />. Refusals are reported as
///     <see cref="IdentityResourceEditOutcome.Protected" /> with a reason, never as "not found".
/// </remarks>
public interface IIdentityResourceEditorService
{
    Task<AdminMutationResult> CreateAsync(CreateIdentityResourceCommand command,
        CancellationToken cancellationToken = default);

    Task<IdentityResourceEditResult> UpdateBasicsAsync(UpdateIdentityResourceBasicsCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates a new Identity Resource. Returns failure if a scope or identity resource with the same name already exists.
    /// </summary>
    Task<AdminMutationResult> CreateAsync(
        string name,
        string? displayName,
        string? description,
        bool enabled,
        bool required,
        bool emphasize,
        bool showInDiscoveryDocument,
        List<string> userClaims,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Loads a single identity resource by name for editing. Returns null if <paramref name="name" />
    ///     does not resolve to an existing identity resource.
    /// </summary>
    Task<IdentityResourceEditorModel?> GetForEditAsync(ScopeName name, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates the display name, description, and flags of an existing identity resource.
    ///     Refused when the resource is protected.
    /// </summary>
    Task<IdentityResourceEditResult> UpdateBasicsAsync(
        string name,
        string? displayName,
        string? description,
        bool enabled,
        bool required,
        bool emphasize,
        bool showInDiscoveryDocument,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Adds a user claim type to the resource. Adding a claim type that already exists is a no-op.
    ///     Refused when the resource is protected, or when the claim type is "openid" (a scope, never
    ///     a user claim).
    /// </summary>
    Task<IdentityResourceEditResult> AddClaimAsync(ScopeName name, ClaimType claimType,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Removes a user claim type from the resource. Refused when the resource is protected, when
    ///     the claim type is "openid", or when the claim is structural to the resource — the
    ///     <c>openid</c> resource keeps its <c>sub</c> claim under every circumstance.
    /// </summary>
    Task<IdentityResourceEditResult> RemoveClaimAsync(ScopeName name, ClaimType claimType,
        CancellationToken cancellationToken = default);
}