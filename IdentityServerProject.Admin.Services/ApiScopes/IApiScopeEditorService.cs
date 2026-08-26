using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.ApiScopes;

public sealed record CreateApiScopeCommand(ScopeName Name, string? DisplayName, string? Description);

public sealed record UpdateApiScopeBasicsCommand(
    ScopeName Name,
    string? DisplayName,
    string? Description,
    bool Enabled,
    bool Required,
    bool Emphasize,
    bool ShowInDiscoveryDocument);

/// <summary>
/// Reads and mutates a single API scope for the Admin &gt; API Scopes edit page.
/// The scope's Name is immutable through this service — there is no rename operation.
/// </summary>
public interface IApiScopeEditorService
{
    Task<AdminMutationResult> CreateAsync(CreateApiScopeCommand command, CancellationToken cancellationToken = default);

    Task<bool> UpdateBasicsAsync(UpdateApiScopeBasicsCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new API scope. Returns failure if a scope or identity resource with the same name already exists.
    /// </summary>
    Task<AdminMutationResult> CreateAsync(
        string name,
        string? displayName,
        string? description,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a single API scope by name for editing. Returns null if <paramref name="name"/>
    /// does not resolve to an existing API scope.
    /// </summary>
    Task<ApiScopeEditorModel?> GetForEditAsync(ScopeName name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the display name and description of an existing API scope. Returns false if
    /// <paramref name="name"/> does not resolve to an existing API scope.
    /// </summary>
    Task<bool> UpdateBasicsAsync(
        string name,
        string? displayName,
        string? description,
        bool enabled,
        bool required,
        bool emphasize,
        bool showInDiscoveryDocument,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a user claim type to the scope. Adding a claim type that already exists is a no-op.</summary>
    Task<bool> AddClaimAsync(ScopeName name, ClaimType claimType, CancellationToken cancellationToken = default);

    /// <summary>Removes a user claim type from the scope.</summary>
    Task<bool> RemoveClaimAsync(ScopeName name, ClaimType claimType, CancellationToken cancellationToken = default);
}
