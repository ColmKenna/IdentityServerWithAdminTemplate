namespace IdentityServerProject.Services.Validation;

/// <summary>
/// Represents a validated absolute HTTP or HTTPS URI.
/// </summary>
public readonly record struct AbsoluteHttpUri
{
    public string Value { get; }

    public AbsoluteHttpUri(string value)
    {
        Value = value ?? string.Empty;
    }

    public static AbsoluteHttpUri Create(string value) => new(value);

    public static bool TryCreate(string? candidate, int maxLength, out AbsoluteHttpUri uri, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            uri = default;
            errorMessage = "URI cannot be empty.";
            return false;
        }

        var trimmed = candidate.Trim();
        if (trimmed.Length > maxLength)
        {
            uri = default;
            errorMessage = $"URI cannot exceed {maxLength} characters.";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            uri = default;
            errorMessage = $"URI '{trimmed}' must be an absolute HTTP or HTTPS URL.";
            return false;
        }

        uri = new AbsoluteHttpUri(trimmed);
        errorMessage = null;
        return true;
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static implicit operator string(AbsoluteHttpUri uri) => uri.Value ?? string.Empty;
    public static explicit operator AbsoluteHttpUri(string value) => Create(value);

    public override string ToString() => Value ?? string.Empty;
}
