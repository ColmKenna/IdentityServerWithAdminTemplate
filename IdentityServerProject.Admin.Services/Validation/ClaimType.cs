namespace IdentityServerProject.Services.Validation;

/// <summary>
///     Strongly typed, validated claim-type name for API resource, API scope, and identity resource
///     user claims. Centralizes the required/max-length rule previously duplicated (and, in one case,
///     missing) across the three editor services.
/// </summary>
public readonly record struct ClaimType(string Value) : IComparable<ClaimType>, IEquatable<ClaimType>
{
    public static readonly ClaimType Empty = new(string.Empty);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public bool IsValid => !IsEmpty && Value.Length <= ValidationConstants.MaxClaimTypeLength;

    public int CompareTo(ClaimType other) => string.Compare(Value, other.Value, StringComparison.Ordinal);

    public static ClaimType Create(string? value) => new(value?.Trim() ?? string.Empty);

    public static implicit operator string(ClaimType claimType) => claimType.Value ?? string.Empty;

    public static explicit operator ClaimType(string? value) => Create(value);

    public override string ToString() => Value ?? string.Empty;
}