namespace IdentityServerProject.Services.Clients;

/// <summary>
///     Client secret metadata, generation, and revocation.
/// </summary>
public interface IClientSecretsService
{
    /// <summary>
    ///     Returns the client secrets (metadata only, never plaintext or hash values) for a client.
    ///     Returns null if no matching client is found.
    /// </summary>
    Task<ClientSecretsModel?> GetClientSecretsAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Generates and persists a new client secret (SHA-256 hashed at rest), returning the plaintext
    ///     value exactly once. Returns Success = false when the client cannot be found.
    /// </summary>
    Task<ClientSecretGenerateResult> GenerateClientSecretAsync(ClientId clientId, string? description,
        DateTime? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Revokes (permanently removes) a client secret. Blocked when the client requires a client
    ///     secret (confidential client) and the target secret is the last remaining one.
    /// </summary>
    Task<ClientSecretRevokeResult> RevokeClientSecretAsync(ClientId clientId, int secretId,
        CancellationToken cancellationToken = default);
}
