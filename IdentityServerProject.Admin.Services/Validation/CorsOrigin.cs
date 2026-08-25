using System;

namespace IdentityServerProject.Services.Validation;

/// <summary>
/// Represents a validated and normalized CORS origin (scheme + authority without path, query, or fragment).
/// </summary>
public readonly record struct CorsOrigin
{
    public string Value { get; }

    public CorsOrigin(string value)
    {
        Value = value ?? string.Empty;
    }

    public static CorsOrigin Create(string value) => new(value);

    public static bool TryCreate(string? candidate, int maxLength, out CorsOrigin origin, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            origin = default;
            errorMessage = "Origin cannot be empty.";
            return false;
        }

        if (!UriValidationHelper.TryNormalizeCorsOrigin(candidate, maxLength, out var normalizedOrigin))
        {
            origin = default;
            errorMessage = $"CORS origin '{candidate}' must contain only an HTTP or HTTPS scheme, host, and optional port.";
            return false;
        }

        origin = new CorsOrigin(normalizedOrigin);
        errorMessage = null;
        return true;
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static implicit operator string(CorsOrigin origin) => origin.Value ?? string.Empty;
    public static explicit operator CorsOrigin(string value) => Create(value);

    public override string ToString() => Value ?? string.Empty;
}
