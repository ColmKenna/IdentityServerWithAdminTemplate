using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.Models;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ClientEntity = Duende.IdentityServer.EntityFramework.Entities.Client;

namespace IdentityServerProject.Admin.Tests.Clients;

/// <summary>
///     The mobile app and IdentityServer have to agree on a client id, two redirect URIs and a set of scopes,
///     and neither side can see the other's code. These tests are where they are compared, so changing one
///     side alone fails here rather than as "sign-in just shows an error" on someone's phone.
/// </summary>
public class MobileClientTests
{
    private static Client Mobile(TokenLifetimes? lifetimes = null) => Config.MobileClient(lifetimes ?? TokenLifetimes.Default);

    [Fact]
    public void ClientIdAndRedirectUris_MatchWhatTheAppSends()
    {
        Client client = Mobile();

        Assert.Equal(IdentityServerProject.Mobile.Core.ClientDefaults.ClientId, client.ClientId);
        Assert.Equal([IdentityServerProject.Mobile.Core.ClientDefaults.RedirectUri], client.RedirectUris);
        Assert.Equal([IdentityServerProject.Mobile.Core.ClientDefaults.PostLogoutRedirectUri], client.PostLogoutRedirectUris);
    }

    [Fact]
    public void TheAppsDefaultScopes_AreAllAllowedForTheClient()
    {
        Client client = Mobile();

        foreach (string scope in IdentityServerProject.Mobile.Core.ClientDefaults.Scope.Split(' '))
        {
            if (scope == "offline_access") Assert.True(client.AllowOfflineAccess);
            else Assert.Contains(scope, client.AllowedScopes);
        }
    }

    [Fact]
    public void ItIsAPublicClient_ThatMustUseCodeWithPkce()
    {
        Client client = Mobile();

        Assert.False(client.RequireClientSecret);
        Assert.Empty(client.ClientSecrets);
        Assert.True(client.RequirePkce);
        Assert.False(client.AllowPlainTextPkce);
        Assert.Equal(GrantTypes.Code, client.AllowedGrantTypes);
    }

    [Fact]
    public void ItCannotUseAnyFlowThatWouldPutATokenInTheRedirectOrBypassTheBrowser()
    {
        Client client = Mobile();

        Assert.DoesNotContain("implicit", client.AllowedGrantTypes);
        Assert.DoesNotContain("password", client.AllowedGrantTypes);
        Assert.DoesNotContain("client_credentials", client.AllowedGrantTypes);
        Assert.False(client.AllowAccessTokensViaBrowser);
    }

    [Fact]
    public void RefreshTokens_AreRotatedOnEveryUse_ButOutliveALongStretchOffline()
    {
        Client client = Mobile();

        Assert.Equal(TokenUsage.OneTimeOnly, client.RefreshTokenUsage);
        Assert.Equal(TokenExpiration.Sliding, client.RefreshTokenExpiration);
        Assert.True(client.SlidingRefreshTokenLifetime >= TimeSpan.FromDays(14).TotalSeconds);
        Assert.True(client.AbsoluteRefreshTokenLifetime > client.SlidingRefreshTokenLifetime);
        Assert.True(client.UpdateAccessTokenClaimsOnRefresh);
    }

    [Fact]
    public void ConfiguredTokenLifetimes_ApplyToTheMobileClientToo()
    {
        Client client = Mobile(new TokenLifetimes(AccessTokenLifetimeSeconds: 900, IdentityTokenLifetimeSeconds: 60, AuthorizationCodeLifetimeSeconds: 90));

        Assert.Equal(900, client.AccessTokenLifetime);
        Assert.Equal(60, client.IdentityTokenLifetime);
        Assert.Equal(90, client.AuthorizationCodeLifetime);
    }

    [Fact]
    public async Task Seeding_StoresTheMobileClient_NeedsNoSecretForIt_AndIsIdempotent()
    {
        using var factory = new AdminWebFactory();

        await SeedAsync(factory);
        await SeedAsync(factory); // second run must not duplicate or fail

        ClientEntity? stored = null;
        await factory.RunInScopeAsync(async sp =>
        {
            stored = await sp.GetRequiredService<ConfigurationDbContext>().Clients
                .AsNoTracking()
                .Include(c => c.RedirectUris)
                .Include(c => c.ClientSecrets)
                .Include(c => c.AllowedScopes)
                .SingleOrDefaultAsync(c => c.ClientId == Config.MobileClientId);
        });

        Assert.NotNull(stored);
        Assert.False(stored!.RequireClientSecret);
        Assert.Empty(stored.ClientSecrets);
        Assert.Contains(stored.RedirectUris, uri => uri.RedirectUri == Config.MobileRedirectUri);
        Assert.Contains(stored.AllowedScopes, scope => scope.Scope == Config.ApiScopeName);
    }

    private static Task SeedAsync(AdminWebFactory factory) =>
        factory.RunInScopeAsync(async sp =>
        {
            var clients = new List<SeedClientSpec>
            {
                new("razorclient", "Example Razor Client", AbsoluteHttpUri.Create("https://localhost:5001"), "secret"),
                new("blazorclient", "Example Blazor Client", AbsoluteHttpUri.Create("https://localhost:5002"), "secret")
            };

            await SeedData.SeedAsync(
                sp.GetRequiredService<ApplicationDbContext>(),
                sp.GetRequiredService<ConfigurationDbContext>(),
                sp.GetRequiredService<PersistedGrantDbContext>(),
                sp.GetRequiredService<UserManager<ApplicationUser>>(),
                sp.GetRequiredService<RoleManager<IdentityRole>>(),
                clients,
                "admin@sales.local",
                "Password123!",
                "Password123!",
                TokenLifetimes.Default);
        });
}
