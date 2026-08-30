namespace IdentityServerProject.Services.Clients;

/// <summary>
///     Service for fetching detailed OAuth/OIDC client configuration and managing client status for the Admin console.
///     Composed of the five per-concern interfaces; prefer depending on the narrowest one that fits.
/// </summary>
public interface IClientDetailsService
    : IClientOverviewService,
        IClientAuthenticationService,
        IClientPermissionsService,
        IClientSecretsService,
        IClientTokenSettingsService
{
}
