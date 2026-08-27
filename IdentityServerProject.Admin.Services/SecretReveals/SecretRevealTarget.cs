namespace IdentityServerProject.Services.SecretReveals;

/// <summary>
///     Binds a secret-reveal purpose to its target identifier, so the two travel together instead of
///     being threaded through <see cref="ISecretRevealService" /> as two separate parameters.
/// </summary>
public readonly record struct SecretRevealTarget(SecretRevealPurpose Purpose, string TargetId);