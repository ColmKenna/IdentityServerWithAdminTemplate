using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdentityServerProject.Services.AuditLogs;

/// <summary>
///     Strongly typed domain value object representing an audit log reason code.
/// </summary>
[JsonConverter(typeof(AuditReasonCodeJsonConverter))]
public readonly record struct AuditReasonCode(string Value)
    : IComparable<AuditReasonCode>, IEquatable<AuditReasonCode>, IParsable<AuditReasonCode>
{
    public static readonly AuditReasonCode Empty = new(string.Empty);

    public static readonly AuditReasonCode Succeeded = new("Succeeded");
    public static readonly AuditReasonCode NotFound = new("NotFound");
    public static readonly AuditReasonCode ValidationFailed = new("ValidationFailed");
    public static readonly AuditReasonCode NameCollision = new("NameCollision");
    public static readonly AuditReasonCode LastAdministrator = new("LastAdministrator");
    public static readonly AuditReasonCode ClientEnabled = new("ClientEnabled");
    public static readonly AuditReasonCode ProtectedResource = new("ProtectedResource");
    public static readonly AuditReasonCode SecretRequired = new("SecretRequired");
    public static readonly AuditReasonCode Expired = new("Expired");
    public static readonly AuditReasonCode WrongContext = new("WrongContext");
    public static readonly AuditReasonCode PersistenceFailure = new("PersistenceFailure");
    public static readonly AuditReasonCode SelfDemotion = new("SelfDemotion");
    public static readonly AuditReasonCode LastUsableSecret = new("LastUsableSecret");
    public static readonly AuditReasonCode RetentionPeriod = new("RetentionPeriod");
    public static readonly AuditReasonCode NotificationFailure = new("NotificationFailure");
    public static readonly AuditReasonCode ReferencedResource = new("ReferencedResource");

    // Project-specific additions
    public static readonly AuditReasonCode SelfAction = new("SelfAction");
    public static readonly AuditReasonCode ReservedClaimType = new("ReservedClaimType");

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public int CompareTo(AuditReasonCode other) =>
        string.Compare(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public static AuditReasonCode Parse(string s, IFormatProvider? provider = null) => From(s);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AuditReasonCode result)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            result = Empty;
            return false;
        }

        result = From(s);
        return true;
    }

    public static AuditReasonCode From(string? value) => new(value?.Trim() ?? string.Empty);

    public static implicit operator string(AuditReasonCode code) => code.Value ?? string.Empty;

    public static explicit operator AuditReasonCode(string? value) => From(value);

    public override string ToString() => Value ?? string.Empty;

    public static bool TryParse([NotNullWhen(true)] string? s, out AuditReasonCode result) =>
        TryParse(s, null, out result);
}

public sealed class AuditReasonCodeJsonConverter : JsonConverter<AuditReasonCode>
{
    public override AuditReasonCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? AuditReasonCode.From(reader.GetString())
            : AuditReasonCode.Empty;
    }

    public override void Write(Utf8JsonWriter writer, AuditReasonCode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value ?? string.Empty);
}