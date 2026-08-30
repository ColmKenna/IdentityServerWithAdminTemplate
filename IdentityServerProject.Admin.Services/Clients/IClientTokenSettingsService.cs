using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Clients;

/// <summary>
///     Token lifetimes and consent / offline-access flags.
/// </summary>
public interface IClientTokenSettingsService
{
    /// <summary>
    ///     Returns access/identity token lifetimes and consent/offline-access flags for a client.
    ///     Returns null if no matching client is found.
    /// </summary>
    Task<ClientTokenSettingsModel?> GetClientTokenSettingsAsync(ClientId clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates access/identity token lifetimes and consent/offline-access flags for a client.
    ///     Returns true if the client was found and updated, false if not found.
    /// </summary>
    Task<AdminMutationResult> UpdateClientTokenSettingsAsync(ClientId clientId, ClientTokenSettingsInputModel input,
        CancellationToken cancellationToken = default);
}
