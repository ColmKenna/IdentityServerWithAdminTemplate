using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdentityServerProject.Services.AuditLogs;

/// <summary>
/// Strongly typed domain value object representing an audit log category.
/// </summary>
[JsonConverter(typeof(AuditCategoryJsonConverter))]
public readonly record struct AuditCategory(string Value) : IComparable<AuditCategory>, IEquatable<AuditCategory>, IParsable<AuditCategory>
{
    public static readonly AuditCategory Empty = new(string.Empty);
    public static readonly AuditCategory User = new("User");
    public static readonly AuditCategory Client = new("Client");
    public static readonly AuditCategory Grant = new("Grant");
    public static readonly AuditCategory ApiResource = new("ApiResource");
    public static readonly AuditCategory ApiScope = new("ApiScope");
    public static readonly AuditCategory IdentityResource = new("IdentityResource");
    public static readonly AuditCategory SecretReveal = new("SecretReveal");
    public static readonly AuditCategory Role = new("Role");

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static AuditCategory Create(string? value) => new(value?.Trim() ?? string.Empty);

    public static implicit operator string(AuditCategory category) => category.Value ?? string.Empty;

    public static explicit operator AuditCategory(string? value) => Create(value);

    public override string ToString() => Value ?? string.Empty;

    public int CompareTo(AuditCategory other) => string.Compare(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public static AuditCategory Parse(string s, IFormatProvider? provider = null) => Create(s);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AuditCategory result)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            result = Empty;
            return false;
        }

        result = Create(s);
        return true;
    }

    public static bool TryParse([NotNullWhen(true)] string? s, out AuditCategory result) =>
        TryParse(s, null, out result);
}

public sealed class AuditCategoryJsonConverter : JsonConverter<AuditCategory>
{
    public override AuditCategory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? AuditCategory.Create(reader.GetString())
            : AuditCategory.Empty;
    }

    public override void Write(Utf8JsonWriter writer, AuditCategory value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value ?? string.Empty);
    }
}
