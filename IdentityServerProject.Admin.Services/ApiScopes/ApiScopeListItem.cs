namespace IdentityServerProject.Services.ApiScopes;

/// <summary>
///     A single row projected for display on the Admin &gt; API Scopes list page.
/// </summary>
public sealed class ApiScopeListItem
{
    public required string Name { get; init; }

    public required string? DisplayName { get; init; }

    public required bool Enabled { get; init; }

    /// <summary>
    ///     Number of distinct clients whose AllowedScopes reference this scope by name.
    /// </summary>
    public required int ClientReferenceCount { get; set; }
}