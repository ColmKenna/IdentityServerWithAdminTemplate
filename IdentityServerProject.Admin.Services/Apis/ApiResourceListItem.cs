namespace IdentityServerProject.Services.Apis;

/// <summary>
/// A single row projected for display on the Admin &gt; API Resources list page.
/// </summary>
public sealed class ApiResourceListItem
{
    public required string Name { get; init; }

    public required string? DisplayName { get; init; }

    public required int ScopeCount { get; init; }

    public required bool Enabled { get; init; }
}
