namespace IdentityServerProject.Services.SecretReveals;

public enum SecretRevealInsertStatus
{
    Inserted,

    /// <summary>
    ///     The generated handle digest collided with an existing row. The caller should generate a
    ///     fresh handle and retry.
    /// </summary>
    DigestCollision
}

public enum SecretRevealLookupStatus
{
    NotFound,
    WrongContext,
    Expired,
    Revealed
}

public sealed record SecretRevealLookup(SecretRevealLookupStatus Status, string? ProtectedPayload)
{
    public static SecretRevealLookup NotFound() => new(SecretRevealLookupStatus.NotFound, null);

    public static SecretRevealLookup WrongContext() => new(SecretRevealLookupStatus.WrongContext, null);

    public static SecretRevealLookup Expired() => new(SecretRevealLookupStatus.Expired, null);

    public static SecretRevealLookup Revealed(string protectedPayload) =>
        new(SecretRevealLookupStatus.Revealed, protectedPayload);
}

/// <summary>
///     Host-owned lifecycle storage for one-time secret-reveal records: insertion (with digest-collision
///     detection so the caller can retry with a fresh handle), atomic match-and-consume, and expiry cleanup.
/// </summary>
public interface ISecretRevealStore
{
    Task<SecretRevealInsertStatus> TryInsertAsync(
        byte[] handleDigest,
        SecretSecurityContext securityContext,
        string protectedPayload,
        DateTimeOffset createdUtc,
        DateTimeOffset expiresUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Atomically loads, validates, and deletes the record for <paramref name=\"handleDigest\" /> so a
    ///     handle can be consumed exactly once even under concurrent callers. A record whose actor,
    ///     purpose, or target does not match is reported as <see cref=\"SecretRevealLookupStatus.WrongContext\" />
    ///     without being deleted, so a legitimate holder can still consume it.
    /// </summary>
    Task<SecretRevealLookup> ConsumeAsync(
        byte[] handleDigest,
        SecretSecurityContext securityContext,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task CleanupExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default);
}