using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdentityServerProject.Services.Roles;

/// <summary>
/// Strongly typed domain identifier for an Identity role.
/// </summary>
[JsonConverter(typeof(RoleIdJsonConverter))]
public readonly record struct RoleId(string Value) : IComparable<RoleId>, IEquatable<RoleId>
{
    public static readonly RoleId Empty = new(string.Empty);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static RoleId Create(string? value) => new(value?.Trim() ?? string.Empty);

    public static implicit operator string(RoleId id) => id.Value ?? string.Empty;

    public static explicit operator RoleId(string? value) => Create(value);

    public override string ToString() => Value ?? string.Empty;

    public int CompareTo(RoleId other) => string.Compare(Value, other.Value, StringComparison.Ordinal);
}

public sealed class RoleIdJsonConverter : JsonConverter<RoleId>
{
    public override RoleId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? RoleId.Create(reader.GetString())
            : RoleId.Empty;
    }

    public override void Write(Utf8JsonWriter writer, RoleId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value ?? string.Empty);
    }
}
