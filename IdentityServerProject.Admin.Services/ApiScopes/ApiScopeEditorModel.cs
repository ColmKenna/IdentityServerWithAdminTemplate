namespace IdentityServerProject.Services.ApiScopes;

/// <summary>
///     Full detail view of a single API scope for the Admin &gt; API Scopes edit page.
/// </summary>
public sealed class ApiScopeEditorModel
{
    public required string Name { get; init; }

    public required string? DisplayName { get; init; }

    public required string? Description { get; init; }

    public bool Enabled { get; set; } = true;

    public bool Required { get; set; }

    public bool Emphasize { get; set; }

    public bool ShowInDiscoveryDocument { get; set; } = true;

    public required List<string> Claims { get; init; }
}