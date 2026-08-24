using Duende.IdentityServer;
using Duende.IdentityServer.Models;

namespace IdentityServerProject;

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

    public static IEnumerable<Client> Clients(string razorClientUri, string razorClientSecret, string blazorClientUri, string blazorClientSecret) =>
        new[]
        {
            new Client
            {
                ClientId = "razorclient",
                ClientName = "Sales Razor Client",
                AllowedGrantTypes = GrantTypes.Code,
                RequirePkce = true,
                ClientSecrets = { new Secret(razorClientSecret.Sha256()) },
                RedirectUris = { $"{razorClientUri}/signin-oidc" },
                PostLogoutRedirectUris = { $"{razorClientUri}/signout-callback-oidc" },
                FrontChannelLogoutUri = $"{razorClientUri}/signout-oidc",
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
            },
            new Client
            {
                ClientId = "blazorclient",
                ClientName = "Sales Blazor Client",
                AllowedGrantTypes = GrantTypes.Code,
                RequirePkce = true,
                ClientSecrets = { new Secret(blazorClientSecret.Sha256()) },
                RedirectUris = { $"{blazorClientUri}/signin-oidc" },
                PostLogoutRedirectUris = { $"{blazorClientUri}/signout-callback-oidc" },
                FrontChannelLogoutUri = $"{blazorClientUri}/signout-oidc",
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
            },
        };
}
