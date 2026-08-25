using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.Secrets;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Clients;

/// <summary>
/// Service for fetching detailed OAuth/OIDC client configuration and managing client status for the Admin console.
/// </summary>
public interface IClientDetailsService
{
    /// <summary>
    /// Returns full configuration details for a client by its ClientId.
    /// Returns null if no matching client is found.
    /// </summary>
    Task<ClientDetailsModel?> GetClientDetailsAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Toggles the Enabled status of a client (Disabled -> Enabled or Enabled -> Disabled).
    /// Returns true if the client was found and updated, false if not found.
    /// </summary>
    Task<bool> ToggleClientStatusAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the basic configuration details (display name and description) of a client by its ClientId.
    /// Returns true if the client was found and updated, false if not found.
    /// </summary>
    Task<AdminMutationResult> UpdateClientBasicsAsync(ClientId clientId, string clientName, string? description, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a client. Blocked unless the client is currently disabled and has been
    /// disabled for at least 90 days. Returns Success = false with a reason when the guard blocks deletion,
    /// or when the client cannot be found.
    /// </summary>
    Task<ClientDeleteResult> DeleteClientAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns grant types, PKCE/secret requirements, redirect URIs, and CORS origins for a client,
    /// along with preset-drift information. Returns null if no matching client is found.
    /// </summary>
    Task<ClientAuthenticationModel?> GetClientAuthenticationAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates grant types, PKCE/secret requirements, redirect URIs, and CORS origins for a client.
    /// Returns success or validation failure.
    /// </summary>
    Task<AdminMutationResult> UpdateClientAuthenticationAsync(ClientId clientId, ClientAuthenticationInputModel input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current allowed scopes for a client along with the available identity resources
    /// and API scopes to choose from. Returns null if no matching client is found.
    /// </summary>
    Task<ClientPermissionsModel?> GetClientPermissionsAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the allowed scopes for a client. For interactive clients, 'openid' is always
    /// enforced regardless of the submitted selection. For M2M clients (client_credentials only),
    /// identity scopes are stripped since they do not apply. Returns true if the client was found
    /// and updated, false if not found.
    /// </summary>
    Task<AdminMutationResult> UpdateClientPermissionsAsync(ClientId clientId, ScopeSet allowedScopes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the client secrets (metadata only, never plaintext or hash values) for a client.
    /// Returns null if no matching client is found.
    /// </summary>
    Task<ClientSecretsModel?> GetClientSecretsAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates and persists a new client secret (SHA-256 hashed at rest), returning the plaintext
    /// value exactly once. Returns Success = false when the client cannot be found.
    /// </summary>
    Task<ClientSecretGenerateResult> GenerateClientSecretAsync(CreateSecretCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates and persists a new client secret (SHA-256 hashed at rest), returning the plaintext
    /// value exactly once. Returns Success = false when the client cannot be found.
    /// </summary>
    Task<ClientSecretGenerateResult> GenerateClientSecretAsync(ClientId clientId, string? description, DateTime? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes (permanently removes) a client secret. Blocked when the client requires a client
    /// secret (confidential client) and the target secret is the last remaining one.
    /// </summary>
    Task<ClientSecretRevokeResult> RevokeClientSecretAsync(ClientId clientId, int secretId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns access/identity token lifetimes and consent/offline-access flags for a client.
    /// Returns null if no matching client is found.
    /// </summary>
    Task<ClientTokenSettingsModel?> GetClientTokenSettingsAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates access/identity token lifetimes and consent/offline-access flags for a client.
    /// Returns true if the client was found and updated, false if not found.
    /// </summary>
    Task<AdminMutationResult> UpdateClientTokenSettingsAsync(ClientId clientId, ClientTokenSettingsInputModel input, CancellationToken cancellationToken = default);
}
