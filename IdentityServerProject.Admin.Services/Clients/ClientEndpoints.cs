using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Clients;

/// <summary>
///     Encapsulates the network endpoints, redirect collections, and CORS origins configured for a client.
/// </summary>
public sealed record ClientEndpoints
{
    public ClientEndpoints()
    {
    }

    public ClientEndpoints(
        IEnumerable<string>? redirectUris = null,
        IEnumerable<string>? postLogoutRedirectUris = null,
        IEnumerable<string>? allowedCorsOrigins = null,
        string? frontChannelLogoutUri = null,
        bool frontChannelLogoutSessionRequired = false,
        string? backChannelLogoutUri = null,
        bool backChannelLogoutSessionRequired = false)
    {
        RedirectUris = redirectUris?.ToList() ?? new List<string>();
        PostLogoutRedirectUris = postLogoutRedirectUris?.ToList() ?? new List<string>();
        AllowedCorsOrigins = allowedCorsOrigins?.ToList() ?? new List<string>();
        FrontChannelLogoutUri = frontChannelLogoutUri;
        FrontChannelLogoutSessionRequired = frontChannelLogoutSessionRequired;
        BackChannelLogoutUri = backChannelLogoutUri;
        BackChannelLogoutSessionRequired = backChannelLogoutSessionRequired;
    }

    public List<string> RedirectUris { get; init; } = new();
    public List<string> PostLogoutRedirectUris { get; init; } = new();
    public List<string> AllowedCorsOrigins { get; init; } = new();
    public string? FrontChannelLogoutUri { get; init; }
    public bool FrontChannelLogoutSessionRequired { get; init; }
    public string? BackChannelLogoutUri { get; init; }
    public bool BackChannelLogoutSessionRequired { get; init; }

    /// <summary>
    ///     Normalizes and deduplicates collections in the client endpoints.
    /// </summary>
    public ClientEndpoints Normalize()
    {
        var normalizedRedirects = (RedirectUris ?? new List<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var normalizedPostLogout = (PostLogoutRedirectUris ?? new List<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var normalizedCors = new List<string>();
        foreach (string origin in (AllowedCorsOrigins ?? new List<string>()).Where(o => !string.IsNullOrWhiteSpace(o)))
            if (UriValidationHelper.TryNormalizeCorsOrigin(origin, ValidationConstants.MaxClientCorsOriginLength,
                    out string norm))
            {
                if (!normalizedCors.Contains(norm, StringComparer.OrdinalIgnoreCase)) normalizedCors.Add(norm);
            }
            else
            {
                string trimmed = origin.Trim();
                if (!normalizedCors.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) normalizedCors.Add(trimmed);
            }

        return this with
        {
            RedirectUris = normalizedRedirects,
            PostLogoutRedirectUris = normalizedPostLogout,
            AllowedCorsOrigins = normalizedCors,
            FrontChannelLogoutUri =
            string.IsNullOrWhiteSpace(FrontChannelLogoutUri) ? null : FrontChannelLogoutUri.Trim(),
            BackChannelLogoutUri = string.IsNullOrWhiteSpace(BackChannelLogoutUri) ? null : BackChannelLogoutUri.Trim()
        };
    }
}