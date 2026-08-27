namespace IdentityServerProject.Services.Secrets;

/// <summary>
/// Unified command for creating/generating a secret on an entity (Client, API Resource, etc.).
/// </summary>
public sealed record CreateSecretCommand(
    string TargetId,
    string? Description = null,
    DateTime? ExpirationUtc = null);
