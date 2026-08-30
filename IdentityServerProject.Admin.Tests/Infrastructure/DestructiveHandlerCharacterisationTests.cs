using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Duende.IdentityServer.EntityFramework.Mappers;
using IdentityServerProject.Data;
using IdentityServerProject.Services.Clients;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

/// <summary>
///     Characterisation test suite for destructive admin handlers (WI-09).
///     Locks in current behavior for destructive handlers before refactoring/re-skinning begins (WI-01..WI-07).
///     All tests adhere to the Should_ExpectedBehaviour_When_Condition naming convention.
/// </summary>
public class DestructiveHandlerCharacterisationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private HttpClient CreateClient(IClientOverviewService clientDetailsService, bool allowAutoRedirect = true)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => { services.AddSingleton(clientDetailsService); });
        });
        _disposables.Add(factory);

        return factory.CreateClient(new WebApplicationFactoryClientOptions
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
        AllowedScopes = new List<string> { "openid", "profile", "coop.market.api" }
    };

    private static async Task<(string Token, string Cookie)> ExtractAntiForgeryTokenAndCookieAsync(
        HttpClient httpClient, string pageUrl)
    {
        HttpResponseMessage response = await httpClient.GetAsync(pageUrl);
        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        IDocument document = await context.OpenAsync(req => req.Content(content));

        var tokenInput = document.QuerySelector("input[name='__RequestVerificationToken']") as IHtmlInputElement;
        Assert.NotNull(tokenInput);

        string token = tokenInput!.Value;
        IEnumerable<string> cookies = response.Headers.GetValues("Set-Cookie");
        string? cookie = cookies.FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery"));
        Assert.NotNull(cookie);

        return (token, cookie!);
    }

    #region Client Status Toggle & Delete Characterisation (US-CLIENT-008 & US-CLIENT-006)

    [Fact]
    public async Task Should_ToggleEnabledState_When_OnPostToggleStatusExecuted()
    {
        var mockService = new Mock<IClientOverviewService>();
        mockService.Setup(s =>
                s.GetClientDetailsAsync(ClientId.Create("coop.market.razor"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleClientDetails());
        mockService.Setup(s =>
                s.ToggleClientStatusAsync(ClientId.Create("coop.market.razor"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        HttpClient client = CreateClient(mockService.Object, false);

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/Clients/Details/coop.market.razor");

        var request = new HttpRequestMessage(HttpMethod.Post,
            "/Admin/Clients/Details/coop.market.razor?handler=ToggleStatus");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        HttpResponseMessage response = await client.SendAsync(request);

        // Asserts redirection back to Details page on successful toggle
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Clients/Details/coop.market.razor", response.Headers.Location?.OriginalString);
        mockService.Verify(
            s => s.ToggleClientStatusAsync(ClientId.Create("coop.market.razor"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_Return404_When_ToggleStatusExecutedForNonExistentClient()
    {
        var mockService = new Mock<IClientOverviewService>();
        mockService.Setup(s => s.GetClientDetailsAsync(ClientId.Create("existing-id"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleClientDetails("existing-id"));
        mockService.Setup(s =>
                s.ToggleClientStatusAsync(ClientId.Create("non-existent-id"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        HttpClient client = CreateClient(mockService.Object, false);

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/Clients/Details/existing-id");

        var request = new HttpRequestMessage(HttpMethod.Post,
            "/Admin/Clients/Details/non-existent-id?handler=ToggleStatus");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        mockService.Verify(
            s => s.ToggleClientStatusAsync(ClientId.Create("non-existent-id"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_PreventSecretRevocation_When_LastSecretOnConfidentialClient()
    {
        // Characterisation rule specification for US-CLIENT-006 (WI-20):
        // Revoke button / handler must be disabled/rejected if the target secret is the last remaining secret on a confidential client.
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        string tag = Guid.NewGuid().ToString("N");
        string clientId = $"{tag}-confidential-single-secret-client";
        int secretId = 0;

        await baseFactory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var entity = new Client
            {
                ClientId = clientId,
                ClientName = "Confidential Single Secret Client",
                RequireClientSecret = true,
                ClientSecrets = new List<ClientSecret>
                {
                    new()
                    {
                        Description = "Only secret", Value = "hashed-value", Type = "SharedSecret",
                        Created = DateTime.UtcNow
                    }
                }
            };
            configDb.Clients.Add(entity);
            await configDb.SaveChangesAsync();
            secretId = entity.ClientSecrets.Single().Id;
        });

        HttpClient client = baseFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Clients/Secrets/{clientId}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Clients/Secrets/{clientId}?handler=Revoke");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["secretId"] = secretId.ToString()
        });

        HttpResponseMessage response = await client.SendAsync(request);

        // Blocked: redirected back to the Secrets page (not a hard failure), and the secret still exists.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Admin/Clients/Secrets/{clientId}", response.Headers.Location?.OriginalString);

        await baseFactory.RunInScopeAsync(async sp =>
        {
            ClientDetailsService service = sp.GetRequiredService<ClientDetailsService>();
            ClientSecretsModel? secrets = await service.GetClientSecretsAsync(ClientId.Create(clientId));
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

        string tag = Guid.NewGuid().ToString("N");
        string clientId = $"{tag}-recently-disabled-client";

        await baseFactory.RunInScopeAsync(async sp =>
        {
            ClientDetailsService service = sp.GetRequiredService<ClientDetailsService>();
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.Clients.Add(new Client
            {
                ClientId = clientId,
                ClientName = "Recently Disabled Client",
                Enabled = true
            });
            await configDb.SaveChangesAsync();

            // Disable via the service so admin:disabledAt is stamped with "now" (< 90 days ago).
            await service.ToggleClientStatusAsync(ClientId.Create(clientId));
        });

        HttpClient client = baseFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Clients/Details/{clientId}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Clients/Details/{clientId}?handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        HttpResponseMessage response = await client.SendAsync(request);

        // Blocked: redirected back to Details rather than to Index, and the client still exists.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Admin/Clients/Details/{clientId}", response.Headers.Location?.OriginalString);

        await baseFactory.RunInScopeAsync(async sp =>
        {
            ClientDetailsService service = sp.GetRequiredService<ClientDetailsService>();
            ClientDetailsModel? stillExists = await service.GetClientDetailsAsync(ClientId.Create(clientId));
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

        string tag = Guid.NewGuid().ToString("N");
        string roleName = $"{tag}-sysadmin";
        string userId = null!;

        await baseFactory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            RoleManager<IdentityRole> roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();

            await roleManager.CreateAsync(new IdentityRole(roleName));

            var user = new ApplicationUser
            {
                UserName = $"{tag}-sole-admin",
                Email = $"{tag}-sole-admin@sales.local",
                EmailConfirmed = true
            };
            await userManager.CreateAsync(user, "Password123!");
            await userManager.AddToRoleAsync(user, roleName);
            userId = user.Id;
        });

        HttpClient client = baseFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Users/Details?id={userId}&tab=roles");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Users/Details?id={userId}&handler=RemoveRole");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["role"] = roleName
        });

        HttpResponseMessage response = await client.SendAsync(request);

        // Successful role changes redirect back to the Roles tab.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("tab=roles", response.Headers.Location?.OriginalString);

        await baseFactory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser? user = await userManager.FindByIdAsync(userId);
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
        string tag = Guid.NewGuid().ToString("N");

        await baseFactory.RunInScopeAsync(async sp =>
        {
            UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = selfUserId,
                UserName = $"admin-{tag}@sales.local",
                Email = $"admin-{tag}@sales.local",
                EmailConfirmed = true
            };
            await userManager.CreateAsync(user, "Password123!");

            PersistedGrantDbContext grantDb = sp.GetRequiredService<PersistedGrantDbContext>();
            grantDb.PersistedGrants.Add(new PersistedGrant
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

        HttpClient client = baseFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Users/Details?id={selfUserId}&tab=access");

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/Admin/Users/Details?id={selfUserId}&handler=RevokeUserAccess");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        HttpResponseMessage response = await client.SendAsync(request);

        // Blocked: redirected back to the Access & grants tab, and the grant still exists.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("tab=access", response.Headers.Location?.OriginalString);

        await baseFactory.RunInScopeAsync(async sp =>
        {
            PersistedGrantDbContext grantDb = sp.GetRequiredService<PersistedGrantDbContext>();
            bool stillExists = await grantDb.PersistedGrants.AnyAsync(g => g.SubjectId == selfUserId);
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
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            IdentityResource entity = new Duende.IdentityServer.Models.IdentityResources.OpenId().ToEntity();
            entity.NonEditable = true;
            configDb.IdentityResources.Add(entity);
            await configDb.SaveChangesAsync();
        });

        HttpClient client = baseFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/IdentityResources/Edit?name=openid");

        var removeRequest = new HttpRequestMessage(HttpMethod.Post,
            "/Admin/IdentityResources/Edit?name=openid&handler=RemoveClaim");
        removeRequest.Headers.Add("Cookie", cookie);
        removeRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["claimType"] = "sub"
        });

        HttpResponseMessage removeResponse = await client.SendAsync(removeRequest);

        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        Assert.DoesNotContain("Location", removeResponse.Headers.Select(h => h.Key));

        // Same token/cookie pair: antiforgery tokens are bound to the session, not to a page, and
        // the cookie is only issued on the first response that needs it.
        var deleteRequest = new HttpRequestMessage(HttpMethod.Post, "/Admin/IdentityResources?handler=Delete");
        deleteRequest.Headers.Add("Cookie", cookie);
        deleteRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = "openid"
        });

        await client.SendAsync(deleteRequest);

        await baseFactory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            IdentityResource? resource = await configDb.IdentityResources
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

        string scopeName = $"api-inuse-{Guid.NewGuid():N}";
        await baseFactory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiScopes.Add(new ApiScope { Name = scopeName, DisplayName = "In Use API" });
            configDb.Clients.Add(new Client
            {
                ClientId = "consumer-client",
                ClientName = "Consumer",
                AllowedScopes = new List<ClientScope>
                {
                    new() { Scope = scopeName }
                }
            });
            await configDb.SaveChangesAsync();
        });

        HttpClient client = baseFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        (string token, string cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/ApiScopes");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/ApiScopes?handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = scopeName,
            ["PageNumber"] = "1"
        });

        HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await baseFactory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            Assert.True(await configDb.ApiScopes.AnyAsync(s => s.Name == scopeName));

            Client? consumerClient = await configDb.Clients.Include(c => c.AllowedScopes)
                .FirstOrDefaultAsync(c => c.ClientId == "consumer-client");
            Assert.NotNull(consumerClient);
            Assert.Contains(consumerClient.AllowedScopes, cs => cs.Scope == scopeName);
        });
    }

    #endregion
}