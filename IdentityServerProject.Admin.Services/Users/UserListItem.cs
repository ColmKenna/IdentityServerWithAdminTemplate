using System;

namespace IdentityServerProject.Services.Users;

/// <summary>
/// A single user row projected for display on the Admin &gt; Users list page.
/// </summary>
public sealed class UserListItem
{
    public required string Id { get; init; }

    public required string UserName { get; init; }

    public required string? Email { get; init; }

    public required string? FullName { get; init; }

    public required bool IsLockedOut { get; init; }

    public required DateTimeOffset? LockoutEnd { get; init; }
}
