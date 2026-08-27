using IdentityServerProject.Services.Users;

namespace IdentityServerProject.Services.Diagnostics;

public class StoreHealthStatus
{
    public required string Name { get; set; }
    public bool IsHealthy { get; set; }
    public string? Detail { get; set; }
}

public class SigningKeySummary
{
    public required string KeyId { get; set; }
    public required string Algorithm { get; set; }
    public bool IsX509Certificate { get; set; }
}

/// <summary>
/// A stored user claim whose type the admin claim editor refuses to create. Such a claim is
/// copied onto the signed-in principal verbatim, so a role-typed one grants privilege that no
/// role-based check — including the Last-Admin Guard — can see.
/// </summary>
public class ReservedClaimHolder
{
    public required UserId UserId { get; set; }
    public required string UserName { get; set; }
    public required string ClaimType { get; set; }
    public required string ClaimValue { get; set; }
}

public class DiagnosticsModel
{
    public List<StoreHealthStatus> StoreHealth { get; set; } = new();
    public string? SigningKeyId { get; set; }
    public string? SigningAlgorithm { get; set; }
    public List<SigningKeySummary> ActiveValidationKeys { get; set; } = new();

    /// <summary>
    /// Users holding a reserved-type user claim. Empty is the expected state.
    /// </summary>
    public List<ReservedClaimHolder> ReservedClaimHolders { get; set; } = new();
}

/// <summary>
/// Reads read-only diagnostics: connectivity health of the Identity/Configuration/Operational
/// stores, the currently active IdentityServer signing credential material, and any user claims
/// of a reserved (framework-owned) type.
/// </summary>
public interface IDiagnosticsService
{
    Task<DiagnosticsModel> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
}
