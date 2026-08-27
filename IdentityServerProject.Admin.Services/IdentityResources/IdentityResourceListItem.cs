namespace IdentityServerProject.Services.IdentityResources;

/// <summary>
///     A single row projected for display on the Admin &gt; Identity Resources list page.
/// </summary>
public sealed class IdentityResourceListItem
{
    public required string Name { get; init; }

    public required string? DisplayName { get; init; }

    public required string? Description { get; init; }

    public required bool Enabled { get; init; }

    public required bool Required { get; init; }

    public required bool Emphasize { get; init; }

    public required bool ShowInDiscoveryDocument { get; init; }

    public required int UserClaimsCount { get; init; }

    /// <summary>
    ///     Number of distinct clients whose AllowedScopes reference this identity resource by name.
    /// </summary>
    public required int ClientReferenceCount { get; set; }

    public required bool NonEditable { get; init; }
}