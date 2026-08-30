using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Clients;

/// <summary>
///     Grant types, PKCE and secret requirements, redirect URIs, and CORS origins.
/// </summary>
public interface IClientAuthenticationService
{
    /// <summary>
    ///     Returns grant types, PKCE/secret requirements, redirect URIs, and CORS origins for a client,
    ///     along with preset-drift information. Returns null if no matching client is found.
    /// </summary>
    Task<ClientAuthenticationModel?> GetClientAuthenticationAsync(ClientId clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates grant types, PKCE/secret requirements, redirect URIs, and CORS origins for a client.
    ///     Returns success or validation failure.
    /// </summary>
    Task<AdminMutationResult> UpdateClientAuthenticationAsync(ClientId clientId, ClientAuthenticationInputModel input,
        CancellationToken cancellationToken = default);
}
