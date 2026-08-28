using IdentityServerProject.Services.Secrets;

namespace IdentityServerProject.Services.Apis;

/// <summary>
///     Full detail view of a single API resource for the Admin &gt; API Resources tabbed editor.
/// </summary>
public sealed class ApiResourceEditorModel
{
    public required bool IsNew { get; init; }

    public required string Name { get; init; }

    public required string? DisplayName { get; init; }

    public required string? Description { get; init; }

    public required bool Enabled { get; init; }

    public required List<ApiResourceSecretItem> Secrets { get; init; }

    public required List<string> Scopes { get; init; }

    public required List<string> Claims { get; init; }

    public static ApiResourceEditorModel Empty() => new()
    {
        IsNew = true,
        Name = string.Empty,
        DisplayName = null,
        Description = null,
        Enabled = true,
        Secrets = [],
        Scopes = [],
        Claims = []
    };
}

/// <summary>
///     A single registered secret on an API resource. <see cref="Value" /> is intentionally
///     omitted; only the hashed value is ever persisted and it is never read back.
/// </summary>
public sealed class ApiResourceSecretItem
{
    public required int Id { get; init; }

    public required string? Description { get; init; }

    public required SecretType Type { get; init; }

    public required DateTime? Expiration { get; init; }

    public required DateTime Created { get; init; }
}