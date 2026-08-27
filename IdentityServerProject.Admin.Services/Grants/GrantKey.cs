using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdentityServerProject.Services.Grants;

/// <summary>
/// Strongly typed domain identifier for a persisted grant key.
/// </summary>
[JsonConverter(typeof(GrantKeyJsonConverter))]
public readonly record struct GrantKey(string Value) : IComparable<GrantKey>, IEquatable<GrantKey>
{
    public static readonly GrantKey Empty = new(string.Empty);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static GrantKey Create(string? value) => new(value?.Trim() ?? string.Empty);

    public static implicit operator string(GrantKey key) => key.Value ?? string.Empty;

    public static explicit operator GrantKey(string? value) => Create(value);

    public override string ToString() => Value ?? string.Empty;

    public int CompareTo(GrantKey other) => string.Compare(Value, other.Value, StringComparison.Ordinal);
}

public sealed class GrantKeyJsonConverter : JsonConverter<GrantKey>
{
    public override GrantKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? GrantKey.Create(reader.GetString())
            : GrantKey.Empty;
    }

    public override void Write(Utf8JsonWriter writer, GrantKey value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value ?? string.Empty);
    }
}
