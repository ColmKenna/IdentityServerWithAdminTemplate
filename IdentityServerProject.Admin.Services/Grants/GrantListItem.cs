using System;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Users;

namespace IdentityServerProject.Services.Grants;

/// <summary>
/// A single row projected for display on the Admin &gt; Persisted Grants list page.
/// </summary>
public sealed class GrantListItem
{
    public required GrantKey Key { get; init; }

    public required string Type { get; init; }

    public required UserId? SubjectId { get; init; }

    public required string? SessionId { get; init; }

    public required ClientId ClientId { get; init; }

    public required string ClientName { get; init; }

    public required string? Description { get; init; }

    public required DateTime CreationTime { get; init; }

    public required DateTime? Expiration { get; init; }

    public required string ExpirationFormatted { get; init; }

    public required bool IsExpired { get; init; }
}
