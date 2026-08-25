using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdentityServerProject.Services.AuditLogs;

/// <summary>
/// Strongly typed domain value object representing an audit log action.
/// </summary>
[JsonConverter(typeof(AuditActionJsonConverter))]
public readonly record struct AuditAction(string Value) : IComparable<AuditAction>, IEquatable<AuditAction>, IParsable<AuditAction>
{
    public static readonly AuditAction Empty = new(string.Empty);

    // Shared across categories
    public static readonly AuditAction Create = new("Create");
    public static readonly AuditAction Update = new("Update");
    public static readonly AuditAction UpdateBasics = new("UpdateBasics");
    public static readonly AuditAction SetEnabled = new("SetEnabled");
    public static readonly AuditAction AddClaim = new("AddClaim");
    public static readonly AuditAction RemoveClaim = new("RemoveClaim");
    public static readonly AuditAction GenerateSecret = new("GenerateSecret");
    public static readonly AuditAction RevokeSecret = new("RevokeSecret");
    public static readonly AuditAction Delete = new("Delete");

    // User
    public static readonly AuditAction UpdatePassword = new("UpdatePassword");
    public static readonly AuditAction ResetPassword = new("ResetPassword");
    public static readonly AuditAction Unlock = new("Unlock");
    public static readonly AuditAction SuspendUser = new("SuspendUser");
    public static readonly AuditAction DeleteUser = new("DeleteUser");
    public static readonly AuditAction AddRole = new("AddRole");
    public static readonly AuditAction RemoveRole = new("RemoveRole");
    public static readonly AuditAction RevokeUserAccess = new("RevokeUserAccess");
    public static readonly AuditAction SendBackChannelLogout = new("SendBackChannelLogout");
    public static readonly AuditAction RemoveGrantsOnUserDelete = new("RemoveGrantsOnUserDelete");

    // Client
    public static readonly AuditAction UpdateAuthentication = new("UpdateAuthentication");
    public static readonly AuditAction UpdatePermissions = new("UpdatePermissions");
    public static readonly AuditAction UpdateTokenSettings = new("UpdateTokenSettings");

    // Grant
    public static readonly AuditAction Revoke = new("Revoke");
    public static readonly AuditAction BulkRevoke = new("BulkRevoke");

    // ApiResource
    public static readonly AuditAction AttachScope = new("AttachScope");
    public static readonly AuditAction CreateScope = new("CreateScope");
    public static readonly AuditAction DetachScope = new("DetachScope");

    // SecretReveal
    public static readonly AuditAction Issue = new("Issue");
    public static readonly AuditAction Consume = new("Consume");

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static AuditAction From(string? value) => new(value?.Trim() ?? string.Empty);

    public static implicit operator string(AuditAction action) => action.Value ?? string.Empty;

    public static implicit operator AuditAction(string? value) => From(value);

    public override string ToString() => Value ?? string.Empty;

    public int CompareTo(AuditAction other) => string.Compare(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public static AuditAction Parse(string s, IFormatProvider? provider = null) => From(s);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AuditAction result)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            result = Empty;
            return false;
        }

        result = From(s);
        return true;
    }

    public static bool TryParse([NotNullWhen(true)] string? s, out AuditAction result) =>
        TryParse(s, null, out result);
}

public sealed class AuditActionJsonConverter : JsonConverter<AuditAction>
{
    public override AuditAction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? AuditAction.From(reader.GetString())
            : AuditAction.Empty;
    }

    public override void Write(Utf8JsonWriter writer, AuditAction value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value ?? string.Empty);
    }
}
