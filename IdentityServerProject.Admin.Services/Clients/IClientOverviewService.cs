using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Clients;

/// <summary>
///     Client overview, basic settings, enable/disable, and deletion.
/// </summary>
public interface IClientOverviewService
{
    /// <summary>
    ///     Returns full configuration details for a client by its ClientId.
    ///     Returns null if no matching client is found.
    /// </summary>
    Task<ClientDetailsModel?> GetClientDetailsAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates the basic configuration details (display name and description) of a client by its ClientId.
    ///     Returns true if the client was found and updated, false if not found.
    /// </summary>
    Task<AdminMutationResult> UpdateClientBasicsAsync(ClientId clientId, string clientName, string? description,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Toggles the Enabled status of a client (Disabled -> Enabled or Enabled -> Disabled).
    ///     Returns true if the client was found and updated, false if not found.
    /// </summary>
    Task<bool> ToggleClientStatusAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Permanently deletes a client. Blocked unless the client is currently disabled and has been
    ///     disabled for at least 90 days. Returns Success = false with a reason when the guard blocks deletion,
    ///     or when the client cannot be found.
    /// </summary>
    Task<ClientDeleteResult> DeleteClientAsync(ClientId clientId, CancellationToken cancellationToken = default);
}
