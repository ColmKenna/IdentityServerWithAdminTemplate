namespace IdentityServerProject.Services.SecretReveals;

public enum SecretRevealPurpose
{
    ClientCreated,
    ClientSecretGenerated,
    ApiResourceSecretGenerated
}

public readonly record struct SecretRevealHandle(string Value)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);
    public static SecretRevealHandle Create(string? value) => new(value?.Trim() ?? string.Empty);
    public static explicit operator SecretRevealHandle(string? value) => Create(value);
    public static implicit operator string(SecretRevealHandle handle) => handle.Value ?? string.Empty;
    public override string ToString() => Value ?? string.Empty;
}

public sealed record SecretRevealTicket(SecretRevealHandle Handle, DateTimeOffset ExpiresUtc);

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
        SecretRevealTarget target,
        string plaintext,
        CancellationToken cancellationToken = default);

    Task<SecretRevealConsumeResult> ConsumeAsync(
        SecretRevealTarget target,
        SecretRevealHandle handle,
        CancellationToken cancellationToken = default);
}