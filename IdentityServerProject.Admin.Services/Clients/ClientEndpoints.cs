using System;
using System.Collections.Generic;
using System.Linq;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Clients;

/// <summary>
/// Encapsulates the network endpoints, redirect collections, and CORS origins configured for a client.
/// </summary>
public sealed record ClientEndpoints
{
    public List<string> RedirectUris { get; init; } = new();
    public List<string> PostLogoutRedirectUris { get; init; } = new();
    public List<string> AllowedCorsOrigins { get; init; } = new();
    public string? FrontChannelLogoutUri { get; init; }
    public bool FrontChannelLogoutSessionRequired { get; init; }
    public string? BackChannelLogoutUri { get; init; }
    public bool BackChannelLogoutSessionRequired { get; init; }

    public ClientEndpoints() { }

    public ClientEndpoints(
        IEnumerable<string>? redirectUris = null,
        IEnumerable<string>? postLogoutRedirectUris = null,
        IEnumerable<string>? allowedCorsOrigins = null,
        string? frontChannelLogoutUri = null,
        bool frontChannelLogoutSessionRequired = false,
        string? backChannelLogoutUri = null,
        bool backChannelLogoutSessionRequired = false)
    {
        RedirectUris = redirectUris?.ToList() ?? new();
        PostLogoutRedirectUris = postLogoutRedirectUris?.ToList() ?? new();
        AllowedCorsOrigins = allowedCorsOrigins?.ToList() ?? new();
        FrontChannelLogoutUri = frontChannelLogoutUri;
        FrontChannelLogoutSessionRequired = frontChannelLogoutSessionRequired;
        BackChannelLogoutUri = backChannelLogoutUri;
        BackChannelLogoutSessionRequired = backChannelLogoutSessionRequired;
    }

    /// <summary>
    /// Normalizes and deduplicates collections in the client endpoints.
    /// </summary>
    public ClientEndpoints Normalize()
    {
        var normalizedRedirects = (RedirectUris ?? new())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var normalizedPostLogout = (PostLogoutRedirectUris ?? new())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var normalizedCors = new List<string>();
        foreach (var origin in (AllowedCorsOrigins ?? new()).Where(o => !string.IsNullOrWhiteSpace(o)))
        {
            if (UriValidationHelper.TryNormalizeCorsOrigin(origin, ValidationConstants.MaxClientCorsOriginLength, out var norm))
            {
                if (!normalizedCors.Contains(norm, StringComparer.OrdinalIgnoreCase))
                {
                    normalizedCors.Add(norm);
                }
            }
            else
            {
                var trimmed = origin.Trim();
                if (!normalizedCors.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    normalizedCors.Add(trimmed);
                }
            }
        }

        return this with
        {
            RedirectUris = normalizedRedirects,
            PostLogoutRedirectUris = normalizedPostLogout,
            AllowedCorsOrigins = normalizedCors,
            FrontChannelLogoutUri = string.IsNullOrWhiteSpace(FrontChannelLogoutUri) ? null : FrontChannelLogoutUri.Trim(),
            BackChannelLogoutUri = string.IsNullOrWhiteSpace(BackChannelLogoutUri) ? null : BackChannelLogoutUri.Trim()
        };
    }
}
