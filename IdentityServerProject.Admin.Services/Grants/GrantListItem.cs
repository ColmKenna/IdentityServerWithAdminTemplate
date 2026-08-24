using System;

namespace IdentityServerProject.Services.Grants;

/// <summary>
/// A single row projected for display on the Admin &gt; Persisted Grants list page.
/// </summary>
public sealed class GrantListItem
{
    public required string Key { get; init; }

    public required string Type { get; init; }

    public required string? SubjectId { get; init; }

    public required string? SessionId { get; init; }

    public required string ClientId { get; init; }

    public required string ClientName { get; init; }

    public required string? Description { get; init; }

    public required DateTime CreationTime { get; init; }

    public required DateTime? Expiration { get; init; }

    public required string ExpirationFormatted { get; init; }

    public required bool IsExpired { get; init; }
}
