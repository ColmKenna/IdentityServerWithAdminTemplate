namespace IdentityServerProject.Mobile.Core;

/// <summary>
///     Values the app and IdentityServer must agree on. The IdentityServer side lives in
///     <c>IdentityServerProject/src/IdentityServerProject/Config.cs</c> (<c>Config.MobileClientId</c> and friends); MobileClientTests in
///     IdentityServerProject.Admin.Tests fails if the two drift apart.
/// </summary>
public static class ClientDefaults
{
    public const string ClientId = "mobile-app";

    /// <summary>
    ///     Reverse-domain private-use scheme (RFC 8252 §7.1). Must be a compile-time constant because
    ///     the Android callback activity declares it in an attribute. Lowercase on purpose: Android matches
    ///     schemes case-sensitively, and browsers lowercase them. scripts/rename-template.ps1 rewrites it.
    /// </summary>
    public const string RedirectScheme = "com.example.identityserverproject";

    public const string RedirectUri = RedirectScheme + "://callback";
    public const string PostLogoutRedirectUri = RedirectScheme + "://signout-callback";

    /// <summary><c>offline_access</c> is what makes IdentityServer issue the refresh token that keeps the device signed in.</summary>
    public const string Scope = "openid profile email roles api offline_access";
}

public sealed class MobileClientOptions
{
    public const string SectionName = "Mobile";

    /// <summary>IdentityServer base address. Must be https; OidcClient refuses discovery over plain http.</summary>
    public string Authority { get; set; } = "https://localhost:5001";

    public string ClientId { get; set; } = ClientDefaults.ClientId;
    public string Scope { get; set; } = ClientDefaults.Scope;
    public string RedirectUri { get; set; } = ClientDefaults.RedirectUri;
    public string PostLogoutRedirectUri { get; set; } = ClientDefaults.PostLogoutRedirectUri;

    /// <summary>Base address of the sync API. Defaults to <see cref="Authority" /> when blank.</summary>
    public string? ApiBaseAddress { get; set; }

    /// <summary>
    ///     Off until a server exposes the sync endpoints described in <see cref="Sync.Contract.SyncRecord" />.
    ///     While off, edits are still queued locally and the app behaves as a fully local app.
    /// </summary>
    public bool SyncEnabled { get; set; }

    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Wait after a local edit before syncing, so a burst of edits becomes one request.</summary>
    public TimeSpan SyncDebounce { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Absolute path of the SQLite file. The host picks it (MAUI: app data directory).</summary>
    public string DatabasePath { get; set; } = "mobile-app.db3";

    public Uri ResolveApiBaseAddress()
    {
        string address = string.IsNullOrWhiteSpace(ApiBaseAddress) ? Authority : ApiBaseAddress;
        return new Uri(address.EndsWith('/') ? address : address + "/");
    }
}
