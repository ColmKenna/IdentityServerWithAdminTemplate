using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Secrets;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Apis;

public sealed record SaveApiResourceBasicsCommand(
    ScopeName? OriginalName,
    ScopeName Name,
    string? DisplayName,
    string? Description);

public sealed record AddApiResourceSecretCommand(
    ScopeName ResourceName,
    string? Description,
    DateTime? ExpirationUtc)
{
    public CreateSecretCommand ToCreateSecretCommand() => new(ResourceName.Value, Description, ExpirationUtc);
}

public sealed record CreateApiResourceScopeCommand(
    ScopeName ResourceName,
    string ScopeName,
    string? DisplayName);

public sealed record AddApiResourceClaimCommand(
    ScopeName ResourceName,
    ClaimType ClaimType);

/// <summary>
/// Reads and mutates a single API resource for the Admin &gt; API Resources tabbed editor
/// (Basics, Secrets, Scopes, Claims).
/// </summary>
public interface IApiResourceEditorService
{
    /// <summary>
    /// Loads a single API resource by name for editing. Returns null if <paramref name=\"name\"/>
    /// does not resolve to an existing API resource.
    /// </summary>
    Task<ApiResourceEditorModel?> GetForEditAsync(ScopeName name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new API resource (when <paramref name=\"command.OriginalName\"/> is null) or updates
    /// the Basics fields of an existing one (when it is not).
    /// </summary>
    Task<SaveApiResourceBasicsResult> SaveBasicsAsync(
        SaveApiResourceBasicsCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a new secret for the target API resource, storing only its SHA-256 hash.
    /// The plaintext value is returned once and is never persisted or retrievable again.
    /// </summary>
    Task<ApiResourceAddSecretResult> AddSecretAsync(
        CreateSecretCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a new secret for the named API resource, storing only its SHA-256 hash.
    /// The plaintext value is returned once and is never persisted or retrievable again.
    /// </summary>
    Task<ApiResourceAddSecretResult> AddSecretAsync(
        AddApiResourceSecretCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a secret by its id.</summary>
    Task<AdminMutationResult> RevokeSecretAsync(ScopeName name, int secretId, CancellationToken cancellationToken = default);

    /// <summary>Attaches an existing system-wide API scope to the resource.</summary>
    Task<AdminMutationResult> AttachScopeAsync(ScopeName name, ScopeName scopeName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new system-wide <c>ApiScope</c> and attaches it to the resource in one step.
    /// Returns conflict if the scope name already exists as either an API scope or an identity resource.</summary>
    Task<AdminMutationResult> CreateScopeAsync(
        CreateApiResourceScopeCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Detaches a scope from the resource. The system-wide ApiScope itself is not deleted.</summary>
    Task<AdminMutationResult> DetachScopeAsync(ScopeName name, ScopeName scopeName, CancellationToken cancellationToken = default);

    /// <summary>Adds a user claim type to the resource.</summary>
    Task<AdminMutationResult> AddClaimAsync(AddApiResourceClaimCommand command, CancellationToken cancellationToken = default);

    /// <summary>Removes a user claim type from the resource.</summary>
    Task<AdminMutationResult> RemoveClaimAsync(ScopeName name, ClaimType claimType, CancellationToken cancellationToken = default);

    /// <summary>Enables or disables the resource.</summary>
    Task<AdminMutationResult> SetEnabledAsync(ScopeName name, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes the API resource, including its secrets, scopes, and claims.</summary>
    Task<AdminMutationResult> DeleteAsync(ScopeName name, CancellationToken cancellationToken = default);

    /// <summary>Returns the names of all system-wide API scopes, for the "attach existing scope" picker.</summary>
    Task<List<string>> GetAllApiScopeNamesAsync(CancellationToken cancellationToken = default);
}
