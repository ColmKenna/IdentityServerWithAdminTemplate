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
}