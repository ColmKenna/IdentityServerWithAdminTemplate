using Duende.IdentityServer;
using Duende.IdentityServer.Models;
using IdentityServerProject.Services.Users;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject;

/// <summary>
///     A seeded client's identity, display name, redirect origin, and shared secret, kept together
///     instead of traveling as four separate parameters from <c>Program.cs</c> through
///     <see cref="Data.SeedData" /> to <see cref="Config.Clients" />.
/// </summary>
public sealed record SeedClientSpec(string ClientId, string ClientName, AbsoluteHttpUri Uri, string Secret);

/// <summary>
///     Token lifetimes applied to the seeded example clients. Duende's own <see cref="Client" />
///     defaults these exact values (3600/300/300) when a Client sets none of them, so this record
///     changes nothing on its own — it exists so <c>IdentityServer:TokenLifetimes</c> in
///     configuration is a real, exercised knob rather than a value nothing reads.
/// </summary>
public sealed record TokenLifetimes(
    int AccessTokenLifetimeSeconds,
    int IdentityTokenLifetimeSeconds,
    int AuthorizationCodeLifetimeSeconds)
{
    public static TokenLifetimes Default { get; } = new(
        AccessTokenLifetimeSeconds: 3600,
        IdentityTokenLifetimeSeconds: 300,
        AuthorizationCodeLifetimeSeconds: 300);
}

public static class Config
{
    public const string ApiScopeName = "api";

    // Owned by the extracted admin-services library (ProtectedAdminRoles) so the host and the
    // library's self-demotion/last-administrator guards can never drift onto different role names.
    public const string SysAdminRole = ProtectedAdminRoles.SysAdmin;

    // The mobile app (IdentityServerProject.Mobile). These must match the app's ClientDefaults — the app sends this
    // client id and these redirect URIs — and MobileClientTests in IdentityServerProject.Admin.Tests fails if they drift apart.
    public const string MobileClientId = "mobile-app";
    public const string MobileRedirectUri = "com.example.identityserverproject://callback";
    public const string MobilePostLogoutRedirectUri = "com.example.identityserverproject://signout-callback";

    public static IEnumerable<IdentityResource> IdentityResources =>
        new[]
        {
            new IdentityResources.OpenId(),
            new IdentityResources.Profile(),
            new IdentityResources.Email(),
            new IdentityResource(
                "roles",
                "Roles",
                new[] { "role" })
        };

    public static IEnumerable<ApiScope> ApiScopes =>
        new[]
        {
            new ApiScope(ApiScopeName, "Default API Scope")
        };

    public static IEnumerable<ApiResource> ApiResources =>
        new[]
        {
            new ApiResource("api-resource", "Default Protected Resource")
            {
                Scopes = { ApiScopeName },
                UserClaims = { "role" }
            }
        };

    public static IEnumerable<Client> Clients(IReadOnlyList<SeedClientSpec> clients, TokenLifetimes tokenLifetimes) =>
        clients.Select(spec => new Client
        {
            ClientId = spec.ClientId,
            ClientName = spec.ClientName,
            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,
            ClientSecrets = { new Secret(spec.Secret.Sha256()) },
            RedirectUris = { $"{spec.Uri}/signin-oidc" },
            PostLogoutRedirectUris = { $"{spec.Uri}/signout-callback-oidc" },
            FrontChannelLogoutUri = $"{spec.Uri}/signout-oidc",
            AllowedScopes =
            {
                IdentityServerConstants.StandardScopes.OpenId,
                IdentityServerConstants.StandardScopes.Profile,
                "email",
                "roles",
                ApiScopeName
            },
            AllowOfflineAccess = true,
            RequireConsent = false,
            AccessTokenLifetime = tokenLifetimes.AccessTokenLifetimeSeconds,
            IdentityTokenLifetime = tokenLifetimes.IdentityTokenLifetimeSeconds,
            AuthorizationCodeLifetime = tokenLifetimes.AuthorizationCodeLifetimeSeconds
        });

    /// <summary>
    ///     The mobile app as an OAuth public client (RFC 8252): authorization code with PKCE through the system
    ///     browser, no client secret because a secret shipped inside an app is not a secret. The refresh token
    ///     is what lets a phone that has been offline for days sync again without asking for a password.
    /// </summary>
    public static Client MobileClient(TokenLifetimes tokenLifetimes) =>
        new()
        {
            ClientId = MobileClientId,
            ClientName = "IdentityServerProject (mobile)",
            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,
            RequireClientSecret = false,
            RedirectUris = { MobileRedirectUri },
            PostLogoutRedirectUris = { MobilePostLogoutRedirectUri },

            // No FrontChannelLogoutUri: an app cannot host the iframe front-channel logout loads.
            AllowedScopes =
            {
                IdentityServerConstants.StandardScopes.OpenId,
                IdentityServerConstants.StandardScopes.Profile,
                "email",
                "roles",
                ApiScopeName
            },
            AllowOfflineAccess = true,
            RequireConsent = false,

            // Rotated on every use, so a copied refresh token stops working as soon as the real device refreshes.
            RefreshTokenUsage = TokenUsage.OneTimeOnly,

            // A phone can be out of coverage for days. Sliding keeps an active device signed in; the absolute
            // cap still forces a fresh sign-in a few months on.
            RefreshTokenExpiration = TokenExpiration.Sliding,
            SlidingRefreshTokenLifetime = (int)TimeSpan.FromDays(30).TotalSeconds,
            AbsoluteRefreshTokenLifetime = (int)TimeSpan.FromDays(90).TotalSeconds,

            // Role changes made in /Admin reach the phone at its next refresh instead of at its next sign-in.
            UpdateAccessTokenClaimsOnRefresh = true,

            AccessTokenLifetime = tokenLifetimes.AccessTokenLifetimeSeconds,
            IdentityTokenLifetime = tokenLifetimes.IdentityTokenLifetimeSeconds,
            AuthorizationCodeLifetime = tokenLifetimes.AuthorizationCodeLifetimeSeconds
        };
}