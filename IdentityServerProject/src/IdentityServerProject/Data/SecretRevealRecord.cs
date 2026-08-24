namespace IdentityServerProject.Data;

public sealed class SecretRevealRecord
{
    public long Id { get; set; }
    public required byte[] HandleDigest { get; set; }
    public required string ActorSubjectId { get; set; }
    public required string Purpose { get; set; }
    public required string TargetId { get; set; }
    public required string ProtectedPayload { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
}
