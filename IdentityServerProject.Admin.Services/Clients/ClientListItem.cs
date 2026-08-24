namespace IdentityServerProject.Services.Clients;

/// <summary>
/// A single row projected for display on the Admin &gt; Clients list page.
/// </summary>
public sealed class ClientListItem
{
    public required string ClientId { get; init; }

    public required string ClientName { get; init; }

    public required string ClientType { get; init; }

    public required bool Enabled { get; init; }
}
