using Duende.IdentityServer;
using Duende.IdentityServer.Models;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject;

/// <summary>
/// A seeded client's identity, display name, redirect origin, and shared secret, kept together
/// instead of traveling as four separate parameters from <c>Program.cs</c> through
/// <see cref="Data.SeedData"/> to <see cref="Config.Clients"/>.
/// </summary>
public sealed record SeedClientSpec(string ClientId, string ClientName, AbsoluteHttpUri Uri, string Secret);

public static class Config
{
    public const string ApiScopeName = "sales.api";

    // Owned by the extracted admin-services library (ProtectedAdminRoles) so the host and the
    // library's self-demotion/last-administrator guards can never drift onto different role names.
    public const string SysAdminRole = Services.Users.ProtectedAdminRoles.SysAdmin;

    public static IEnumerable<IdentityResource> IdentityResources =>
        new IdentityResource[]
        {
            new IdentityResources.OpenId(),
            new IdentityResources.Profile(),
            new IdentityResources.Email(),
            new IdentityResource(
                name: "roles",
                displayName: "Roles",
                userClaims: new[] { "role" }),
        };

    public static IEnumerable<ApiScope> ApiScopes =>
        new[]
        {
            new ApiScope(ApiScopeName, "Sales API"),
        };

    public static IEnumerable<ApiResource> ApiResources =>
        new[]
        {
            new ApiResource("sales", "Sales API Resource")
            {
                Scopes = { ApiScopeName },
                UserClaims = { "role" },
            },
        };

    public static IEnumerable<Client> Clients(IReadOnlyList<SeedClientSpec> clients) =>
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
                ApiScopeName,
            },
            AllowOfflineAccess = true,
            RequireConsent = false,
        });
}
