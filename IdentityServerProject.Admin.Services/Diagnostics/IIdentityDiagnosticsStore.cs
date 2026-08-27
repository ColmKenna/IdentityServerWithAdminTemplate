namespace IdentityServerProject.Services.Diagnostics;

/// <summary>
///     Host-owned read access to Identity-store connectivity and user-claim diagnostics.
/// </summary>
public interface IIdentityDiagnosticsStore
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     The distinct set of user claim types currently stored, so the reserved-type
    ///     policy can be evaluated in memory without needing provider-specific case-insensitive
    ///     or prefix matching pushed down to SQL.
    /// </summary>
    Task<IReadOnlyList<string>> GetDistinctUserClaimTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Every user holding a claim of one of the given (already reserved-filtered) types.
    /// </summary>
    Task<IReadOnlyList<ReservedClaimHolder>> GetClaimHoldersAsync(
        IReadOnlyCollection<string> claimTypes,
        CancellationToken cancellationToken = default);
}