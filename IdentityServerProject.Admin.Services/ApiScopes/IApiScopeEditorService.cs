namespace IdentityServerProject.Services.ApiScopes;

public class ApiScopeCreateResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static ApiScopeCreateResult Failed(string errorMessage) => new() { Success = false, ErrorMessage = errorMessage };
    public static ApiScopeCreateResult Succeeded() => new() { Success = true };
}

/// <summary>
/// Reads and mutates a single API scope for the Admin &gt; API Scopes edit page.
/// The scope's Name is immutable through this service — there is no rename operation.
/// </summary>
public interface IApiScopeEditorService
{
    /// <summary>
    /// Creates a new API scope. Returns failure if a scope or identity resource with the same name already exists.
    /// </summary>
    Task<ApiScopeCreateResult> CreateAsync(
        string name,
        string? displayName,
        string? description,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a single API scope by name for editing. Returns null if <paramref name="name"/>
    /// does not resolve to an existing API scope.
    /// </summary>
    Task<ApiScopeEditorModel?> GetForEditAsync(string name, CancellationToken cancellationToken = default);

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
    Task<bool> AddClaimAsync(string name, string claimType, CancellationToken cancellationToken = default);

    /// <summary>Removes a user claim type from the scope.</summary>
    Task<bool> RemoveClaimAsync(string name, string claimType, CancellationToken cancellationToken = default);
}
