using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using Duende.IdentityServer.EntityFramework.Mappers;
using IdentityServerProject.Services.Clients;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

/// <summary>
/// Characterisation test suite for destructive admin handlers (WI-09).
/// Locks in current behavior for destructive handlers before refactoring/re-skinning begins (WI-01..WI-07).
/// All tests adhere to the Should_ExpectedBehaviour_When_Condition naming convention.
/// </summary>
public class DestructiveHandlerCharacterisationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    private HttpClient CreateClient(IClientDetailsService clientDetailsService, bool allowAutoRedirect = true)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(clientDetailsService);
            });
        });
        _disposables.Add(factory);

        return factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
    }

    private static ClientDetailsModel SampleClientDetails(string id = "coop.market.razor", bool enabled = true) => new()
    {
        ClientId = ClientId.Create(id),
        ClientName = "Co-op Market Razor Client",
        Description = "Sample description",
        ClientType = "SPA with BFF",
        Enabled = enabled,
        RequirePkce = true,
        RequireClientSecret = true,
        RequireConsent = false,
        AllowOfflineAccess = true,
        AccessTokenLifetime = TokenLifetime.FromSeconds(300),
        AllowedGrantTypes = "authorization_code",
        RedirectUrisCount = 2,
        CorsOriginsCount = 0,
        SecretsCount = 1,
        AllowedScopesCount = 3,
        AllowedScopes = new() { "openid", "profile", "coop.market.api" }
    };

    private static async Task<(string Token, string Cookie)> ExtractAntiForgeryTokenAndCookieAsync(HttpClient httpClient, string pageUrl)
    {
        var response = await httpClient.GetAsync(pageUrl);
        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(content));

        var tokenInput = document.QuerySelector("input[name='__RequestVerificationToken']") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(tokenInput);

        var token = tokenInput!.Value;
        var cookies = response.Headers.GetValues("Set-Cookie");
        var cookie = cookies.FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery"));
        Assert.NotNull(cookie);

        return (token, cookie!);
    }

    #region Client Status Toggle & Delete Characterisation (US-CLIENT-008 & US-CLIENT-006)

    [Fact]
    public async Task Should_ToggleEnabledState_When_OnPostToggleStatusExecuted()
    {
        var mockService = new Mock<IClientDetailsService>();
        mockService.Setup(s => s.GetClientDetailsAsync(ClientId.Create("coop.market.razor"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleClientDetails("coop.market.razor", enabled: true));
        mockService.Setup(s => s.ToggleClientStatusAsync(ClientId.Create("coop.market.razor"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var client = CreateClient(mockService.Object, allowAutoRedirect: false);

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/Clients/Details/coop.market.razor");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Details/coop.market.razor?handler=ToggleStatus");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await client.SendAsync(request);

        // Asserts redirection back to Details page on successful toggle
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Clients/Details/coop.market.razor", response.Headers.Location?.OriginalString);
        mockService.Verify(s => s.ToggleClientStatusAsync(ClientId.Create("coop.market.razor"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_Return404_When_ToggleStatusExecutedForNonExistentClient()
    {
        var mockService = new Mock<IClientDetailsService>();
        mockService.Setup(s => s.GetClientDetailsAsync(ClientId.Create("existing-id"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleClientDetails("existing-id", enabled: true));
        mockService.Setup(s => s.ToggleClientStatusAsync(ClientId.Create("non-existent-id"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var client = CreateClient(mockService.Object, allowAutoRedirect: false);

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/Clients/Details/existing-id");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Details/non-existent-id?handler=ToggleStatus");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        mockService.Verify(s => s.ToggleClientStatusAsync(ClientId.Create("non-existent-id"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_PreventSecretRevocation_When_LastSecretOnConfidentialClient()
    {
        // Characterisation rule specification for US-CLIENT-006 (WI-20):
        // Revoke button / handler must be disabled/rejected if the target secret is the last remaining secret on a confidential client.
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var tag = Guid.NewGuid().ToString("N");
        var clientId = $"{tag}-confidential-single-secret-client";
        int secretId = 0;

        await baseFactory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<Duende.IdentityServer.EntityFramework.DbContexts.ConfigurationDbContext>();
            var entity = new Duende.IdentityServer.EntityFramework.Entities.Client
            {
                ClientId = clientId,
                ClientName = "Confidential Single Secret Client",
                RequireClientSecret = true,
                ClientSecrets = new List<Duende.IdentityServer.EntityFramework.Entities.ClientSecret>
                {
                    new() { Description = "Only secret", Value = "hashed-value", Type = "SharedSecret", Created = DateTime.UtcNow }
                }
            };
            configDb.Clients.Add(entity);
            await configDb.SaveChangesAsync();
            secretId = entity.ClientSecrets.Single().Id;
        });

        var client = baseFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Clients/Secrets/{clientId}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Clients/Secrets/{clientId}?handler=Revoke");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["secretId"] = secretId.ToString()
        });

        var response = await client.SendAsync(request);

        // Blocked: redirected back to the Secrets page (not a hard failure), and the secret still exists.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Admin/Clients/Secrets/{clientId}", response.Headers.Location?.OriginalString);

        await baseFactory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var secrets = await service.GetClientSecretsAsync(ClientId.Create(clientId));
            Assert.NotNull(secrets);
            Assert.Single(secrets!.Secrets);
        });
    }

    [Fact]
    public async Task Should_PreventClientDeletion_When_DisabledLessThan90Days()
    {
        // Characterisation rule specification for US-CLIENT-008 (WI-22):
        // Client deletion must be blocked if the client has been disabled for fewer than 90 days.
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var tag = Guid.NewGuid().ToString("N");
        var clientId = $"{tag}-recently-disabled-client";

        await baseFactory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var configDb = sp.GetRequiredService<Duende.IdentityServer.EntityFramework.DbContexts.ConfigurationDbContext>();
            configDb.Clients.Add(new Duende.IdentityServer.EntityFramework.Entities.Client
            {
                ClientId = clientId,
                ClientName = "Recently Disabled Client",
                Enabled = true,
            });
            await configDb.SaveChangesAsync();

            // Disable via the service so admin:disabledAt is stamped with "now" (< 90 days ago).
            await service.ToggleClientStatusAsync(ClientId.Create(clientId));
        });

        var client = baseFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Clients/Details/{clientId}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Clients/Details/{clientId}?handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await client.SendAsync(request);

        // Blocked: redirected back to Details rather than to Index, and the client still exists.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Admin/Clients/Details/{clientId}", response.Headers.Location?.OriginalString);

        await baseFactory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IClientDetailsService>();
            var stillExists = await service.GetClientDetailsAsync(ClientId.Create(clientId));
            Assert.NotNull(stillExists);
        });
    }

    #endregion

    #region User Management Characterisation (US-USER-003)

    [Fact]
    public async Task Should_AllowLastOrdinaryRoleRemoval_When_UserIsSoleRoleHolder()
    {
        // Characterisation rule specification for US-USER-003 (WI-31):
        // TASK-02 scopes the last-holder guard specifically to Config.SysAdminRole. An ordinary
        // role may be removed from its final holder.
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var tag = Guid.NewGuid().ToString("N");
        var roleName = $"{tag}-sysadmin";
        string userId = null!;

        await baseFactory.RunInScopeAsync(async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<IdentityServerProject.Data.ApplicationUser>>();
            var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();

            await roleManager.CreateAsync(new IdentityRole(roleName));

            var user = new IdentityServerProject.Data.ApplicationUser
            {
                UserName = $"{tag}-sole-admin",
                Email = $"{tag}-sole-admin@sales.local",
                EmailConfirmed = true
            };
            await userManager.CreateAsync(user, "Password123!");
            await userManager.AddToRoleAsync(user, roleName);
            userId = user.Id;
        });

        var client = baseFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Users/Details?id={userId}&tab=roles");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Users/Details?id={userId}&handler=RemoveRole");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["role"] = roleName
        });

        var response = await client.SendAsync(request);

        // Successful role changes redirect back to the Roles tab.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("tab=roles", response.Headers.Location?.OriginalString);

        await baseFactory.RunInScopeAsync(async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<IdentityServerProject.Data.ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId);
            Assert.NotNull(user);
            Assert.False(await userManager.IsInRoleAsync(user!, roleName));
        });
    }

    [Fact]
    public async Task Should_PreventSessionRevocation_When_ViewingSelf()
    {
        // Characterisation rule specification for US-USER-003 (WI-31):
        // An administrator must not be permitted to revoke their own access from the Users workspace
        // (self-lockout prevention - TestAuthHandler authenticates as NameIdentifier "admin-test-id").
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        const string selfUserId = "admin-test-id";
        var tag = Guid.NewGuid().ToString("N");

        await baseFactory.RunInScopeAsync(async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<IdentityServerProject.Data.ApplicationUser>>();
            var user = new IdentityServerProject.Data.ApplicationUser
            {
                Id = selfUserId,
                UserName = $"admin-{tag}@sales.local",
                Email = $"admin-{tag}@sales.local",
                EmailConfirmed = true
            };
            await userManager.CreateAsync(user, "Password123!");

            var grantDb = sp.GetRequiredService<Duende.IdentityServer.EntityFramework.DbContexts.PersistedGrantDbContext>();
            grantDb.PersistedGrants.Add(new Duende.IdentityServer.EntityFramework.Entities.PersistedGrant
            {
                Key = $"{tag}-self-grant",
                Type = "user_consent",
                ClientId = $"{tag}-client",
                SubjectId = selfUserId,
                CreationTime = DateTime.UtcNow,
                Expiration = DateTime.UtcNow.AddDays(1),
                Data = "{}"
            });
            await grantDb.SaveChangesAsync();
        });

        var client = baseFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Users/Details?id={selfUserId}&tab=access");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Users/Details?id={selfUserId}&handler=RevokeUserAccess");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await client.SendAsync(request);

        // Blocked: redirected back to the Access & grants tab, and the grant still exists.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("tab=access", response.Headers.Location?.OriginalString);

        await baseFactory.RunInScopeAsync(async sp =>
        {
            var grantDb = sp.GetRequiredService<Duende.IdentityServer.EntityFramework.DbContexts.PersistedGrantDbContext>();
            var stillExists = await grantDb.PersistedGrants.AnyAsync(g => g.SubjectId == selfUserId);
            Assert.True(stillExists);
        });
    }

    #endregion

    #region Identity Resources & Scopes Characterisation (US-IDRES-002, US-SCOPE-001/002)

    [Fact]
    public async Task Should_PreventClaimDeletion_When_OpenIdClaim()
    {
        // Characterisation rule for US-IDRES-002:
        // The core 'openid' identity resource is immutable — its 'sub' claim cannot be removed and
        // the resource itself cannot be deleted.
        //
        // Built from Duende's own IdentityResources.OpenId() rather than a hand-built row: the
        // defect this locks in against was a guard that matched the claim literal "openid" instead
        // of the resource, and a fabricated row would have satisfied that guard too.
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        await baseFactory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<Duende.IdentityServer.EntityFramework.DbContexts.ConfigurationDbContext>();
            var entity = new Duende.IdentityServer.Models.IdentityResources.OpenId().ToEntity();
            entity.NonEditable = true;
            configDb.IdentityResources.Add(entity);
            await configDb.SaveChangesAsync();
        });

        var client = baseFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/IdentityResources/Edit?name=openid");

        var removeRequest = new HttpRequestMessage(HttpMethod.Post, "/Admin/IdentityResources/Edit?name=openid&handler=RemoveClaim");
        removeRequest.Headers.Add("Cookie", cookie);
        removeRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["claimType"] = "sub",
        });

        var removeResponse = await client.SendAsync(removeRequest);

        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        Assert.DoesNotContain("Location", removeResponse.Headers.Select(h => h.Key));

        // Same token/cookie pair: antiforgery tokens are bound to the session, not to a page, and
        // the cookie is only issued on the first response that needs it.
        var deleteRequest = new HttpRequestMessage(HttpMethod.Post, "/Admin/IdentityResources?handler=Delete");
        deleteRequest.Headers.Add("Cookie", cookie);
        deleteRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = "openid",
        });

        await client.SendAsync(deleteRequest);

        await baseFactory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<Duende.IdentityServer.EntityFramework.DbContexts.ConfigurationDbContext>();
            var resource = await configDb.IdentityResources
                .Include(r => r.UserClaims)
                .FirstOrDefaultAsync(r => r.Name == "openid");

            Assert.NotNull(resource);
            Assert.Contains(resource!.UserClaims, c => c.Type == "sub");
            Assert.True(resource.Enabled);
        });
    }

    [Fact]
    public async Task Should_BlockScopeDeletion_When_ReferencedByClients()
    {
        using var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var scopeName = $"api-inuse-{Guid.NewGuid():N}";
        await baseFactory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<Duende.IdentityServer.EntityFramework.DbContexts.ConfigurationDbContext>();
            configDb.ApiScopes.Add(new Duende.IdentityServer.EntityFramework.Entities.ApiScope { Name = scopeName, DisplayName = "In Use API" });
            configDb.Clients.Add(new Duende.IdentityServer.EntityFramework.Entities.Client
            {
                ClientId = "consumer-client",
                ClientName = "Consumer",
                AllowedScopes = new List<Duende.IdentityServer.EntityFramework.Entities.ClientScope>
                {
                    new Duende.IdentityServer.EntityFramework.Entities.ClientScope { Scope = scopeName }
                }
            });
            await configDb.SaveChangesAsync();
        });

        var client = baseFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/ApiScopes");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/ApiScopes?handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = scopeName,
            ["PageNumber"] = "1",
        });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await baseFactory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<Duende.IdentityServer.EntityFramework.DbContexts.ConfigurationDbContext>();
            Assert.True(await configDb.ApiScopes.AnyAsync(s => s.Name == scopeName));
            
            var consumerClient = await configDb.Clients.Include(c => c.AllowedScopes).FirstOrDefaultAsync(c => c.ClientId == "consumer-client");
            Assert.NotNull(consumerClient);
            Assert.Contains(consumerClient.AllowedScopes, cs => cs.Scope == scopeName);
        });
    }

    #endregion

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}
