namespace IdentityServerProject.Services.SecretReveals;

public enum SecretRevealPurpose
{
    ClientCreated,
    ClientSecretGenerated,
    ApiResourceSecretGenerated
}

public sealed record SecretRevealTicket(string Handle, DateTimeOffset ExpiresUtc);

public enum SecretRevealConsumeStatus
{
    Revealed,
    Unavailable
}

public sealed record SecretRevealConsumeResult(
    SecretRevealConsumeStatus Status,
    string? Plaintext)
{
    public static SecretRevealConsumeResult Revealed(string plaintext) =>
        new(SecretRevealConsumeStatus.Revealed, plaintext);

    public static SecretRevealConsumeResult Unavailable() =>
        new(SecretRevealConsumeStatus.Unavailable, null);
}

public interface ISecretRevealService
{
    Task<SecretRevealTicket> IssueAsync(
        SecretRevealPurpose purpose,
        string targetId,
        string plaintext,
        CancellationToken cancellationToken = default);

    Task<SecretRevealConsumeResult> ConsumeAsync(
        SecretRevealPurpose purpose,
        string targetId,
        string handle,
        CancellationToken cancellationToken = default);
}
