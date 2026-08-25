using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Scopes;

/// <summary>
/// Strongly typed domain identifier for an OAuth/OIDC scope (API scope or Identity resource).
/// </summary>
[JsonConverter(typeof(ScopeNameJsonConverter))]
public readonly record struct ScopeName(string Value) : IComparable<ScopeName>, IEquatable<ScopeName>
{
    public static readonly ScopeName Empty = new(string.Empty);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public bool IsValid => !IsEmpty && Value.Length <= ValidationConstants.MaxScopeNameLength && ScopeValidationHelper.IsValidScopeName(Value);

    public static ScopeName Create(string? value) => new(value?.Trim() ?? string.Empty);

    public static implicit operator string(ScopeName scope) => scope.Value ?? string.Empty;

    public static implicit operator ScopeName(string? value) => Create(value);

    public override string ToString() => Value ?? string.Empty;

    public int CompareTo(ScopeName other) => string.Compare(Value, other.Value, StringComparison.Ordinal);
}

public sealed class ScopeNameJsonConverter : JsonConverter<ScopeName>
{
    public override ScopeName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? ScopeName.Create(reader.GetString())
            : ScopeName.Empty;
    }

    public override void Write(Utf8JsonWriter writer, ScopeName value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value ?? string.Empty);
    }
}
