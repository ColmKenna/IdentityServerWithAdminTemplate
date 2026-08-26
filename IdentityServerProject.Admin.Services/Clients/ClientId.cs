using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Clients;

/// <summary>
/// Strongly typed domain identifier for an IdentityServer client.
/// </summary>
[JsonConverter(typeof(ClientIdJsonConverter))]
public readonly record struct ClientId(string Value) : IComparable<ClientId>, IEquatable<ClientId>
{
    public static readonly ClientId Empty = new(string.Empty);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public bool IsValid => !IsEmpty && Value.Length <= ValidationConstants.MaxClientIdLength;

    public static ClientId Create(string? value) => new(value?.Trim() ?? string.Empty);

    public static implicit operator string(ClientId id) => id.Value ?? string.Empty;

    public static explicit operator ClientId(string? value) => Create(value);

    public override string ToString() => Value ?? string.Empty;

    public int CompareTo(ClientId other) => string.Compare(Value, other.Value, StringComparison.Ordinal);
}

public sealed class ClientIdJsonConverter : JsonConverter<ClientId>
{
    public override ClientId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? ClientId.Create(reader.GetString())
            : ClientId.Empty;
    }

    public override void Write(Utf8JsonWriter writer, ClientId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value ?? string.Empty);
    }
}
