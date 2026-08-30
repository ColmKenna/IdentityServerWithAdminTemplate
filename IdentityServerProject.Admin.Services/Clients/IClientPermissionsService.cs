using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Clients;

/// <summary>
///     The scopes a client is allowed to request.
/// </summary>
public interface IClientPermissionsService
{
    /// <summary>
    ///     Returns the current allowed scopes for a client along with the available identity resources
    ///     and API scopes to choose from. Returns null if no matching client is found.
    /// </summary>
    Task<ClientPermissionsModel?> GetClientPermissionsAsync(ClientId clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates the allowed scopes for a client. For interactive clients, 'openid' is always
    ///     enforced regardless of the submitted selection. For M2M clients (client_credentials only),
    ///     identity scopes are stripped since they do not apply. Returns true if the client was found
    ///     and updated, false if not found.
    /// </summary>
    Task<AdminMutationResult> UpdateClientPermissionsAsync(ClientId clientId, ScopeSet allowedScopes,
        CancellationToken cancellationToken = default);
}
