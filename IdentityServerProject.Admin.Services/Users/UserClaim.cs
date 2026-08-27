namespace IdentityServerProject.Services.Users;

/// <summary>
///     Strongly typed domain value object representing a user claim type and value pair.
/// </summary>
public readonly record struct UserClaim(string Type, string Value)
{
    public static implicit operator UserClaim((string Type, string Value) tuple) => new(tuple.Type, tuple.Value);
    public static implicit operator (string Type, string Value)(UserClaim claim) => (claim.Type, claim.Value);

    public void Deconstruct(out string type, out string value)
    {
        type = Type;
        value = Value;
    }
}