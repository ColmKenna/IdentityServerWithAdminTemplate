using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdentityServerProject.Services.Users;

/// <summary>
/// Strongly typed domain identifier for an Identity user subject.
/// </summary>
[JsonConverter(typeof(UserIdJsonConverter))]
public readonly record struct UserId(string Value) : IComparable<UserId>, IEquatable<UserId>
{
    public static readonly UserId Empty = new(string.Empty);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static UserId Create(string? value) => new(value?.Trim() ?? string.Empty);

    public static implicit operator string(UserId id) => id.Value ?? string.Empty;

    public static explicit operator UserId(string? value) => Create(value);

    public override string ToString() => Value ?? string.Empty;

    public int CompareTo(UserId other) => string.Compare(Value, other.Value, StringComparison.Ordinal);
}

public sealed class UserIdJsonConverter : JsonConverter<UserId>
{
    public override UserId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? UserId.Create(reader.GetString())
            : UserId.Empty;
    }

    public override void Write(Utf8JsonWriter writer, UserId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value ?? string.Empty);
    }
}
