namespace IdentityServerProject.Services.Validation;

public static class UriValidationHelper
{
    public static bool IsValidHttpOrHttpsUri(string? uri, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return false;

        var trimmed = uri.Trim();
        if (trimmed.Length > maxLength)
            return false;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
            return false;

        return parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps;
    }

    public static IEnumerable<string> GetInvalidHttpUris(IEnumerable<string>? uris, int maxLength)
    {
        if (uris == null)
            return Enumerable.Empty<string>();

        return uris
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Where(u => !IsValidHttpOrHttpsUri(u, maxLength));
    }

    public static bool TryNormalizeCorsOrigin(string? origin, int maxLength, out string normalizedOrigin)
    {
        normalizedOrigin = string.Empty;
        if (!IsValidHttpOrHttpsUri(origin, maxLength))
            return false;

        var parsed = new Uri(origin!.Trim(), UriKind.Absolute);
        if (!string.IsNullOrEmpty(parsed.UserInfo)
            || parsed.AbsolutePath != "/"
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment))
        {
            return false;
        }

        normalizedOrigin = parsed.GetLeftPart(UriPartial.Authority);
        return normalizedOrigin.Length <= maxLength;
    }
}
