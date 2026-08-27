using IdentityServerProject.Services.Users;

namespace IdentityServerProject.Services.SecretReveals;

/// <summary>
/// Security context binding the actor subject, purpose, and target identifier for secret reveal operations.
/// </summary>
public sealed record SecretSecurityContext(
    UserId ActorSubjectId,
    SecretRevealPurpose Purpose,
    string TargetId)
{
    public static SecretSecurityContext Create(UserId actorSubjectId, SecretRevealPurpose purpose, string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            throw new ArgumentException("A non-empty target ID is required.", nameof(targetId));

        return new(actorSubjectId, purpose, targetId.Trim());
    }
}
