using System.Net;
using System.Security.Claims;
using IdentityServerProject.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

public class AdminAuthorizationIntegrationTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public AdminAuthorizationIntegrationTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AnonymousRequest_ToAdminRoute_RedirectsToLogin()
    {
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-Auth", "anonymous");

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login?returnUrl=", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AuthenticatedNonAdmin_ToAdminRoute_IsRedirectedToAccessDenied()
    {
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-Auth", "non-admin");

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AccessDeniedPage_ExplainsTheMissingAdminPermission()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "non-admin");

        HttpResponseMessage response = await client.GetAsync("/Account/AccessDenied");
        string content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("does not have permission", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SysAdmin_ToAdminRoute_IsAllowed()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void EveryAdminRazorPage_RequiresTheSysAdminOnlyPolicy()
    {
        var adminPageEndpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Select(endpoint => new
            {
                Page = endpoint.Metadata.GetMetadata<PageActionDescriptor>(),
                Authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            })
            .Where(endpoint =>
                endpoint.Page?.ViewEnginePath.StartsWith("/Admin/", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.NotEmpty(adminPageEndpoints);
        Assert.All(adminPageEndpoints, endpoint =>
            Assert.Contains(endpoint.Authorization, authorization => authorization.Policy == "SysAdminOnly"));
    }

    [Fact]
    public async Task SysAdmin_HasTheRoleClaimUsedByCookieSignIn()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        UserManager<ApplicationUser> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager<IdentityRole> roleManager =
            scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        IUserClaimsPrincipalFactory<ApplicationUser> principalFactory =
            scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();

        // Create role and user explicitly — the host no longer seeds admins on startup.
        if (!await roleManager.RoleExistsAsync(Config.SysAdminRole))
            await roleManager.CreateAsync(new IdentityRole(Config.SysAdminRole));

        string adminEmail = "role-claim-test-admin@sales.local";
        var admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FullName = "Role Claim Test Admin"
        };
        await userManager.CreateAsync(admin, "Password123!");
        await userManager.AddToRoleAsync(admin, Config.SysAdminRole);

        Assert.True(await userManager.IsInRoleAsync(admin, Config.SysAdminRole));

        ClaimsPrincipal principal = await principalFactory.CreateAsync(admin);
        Assert.True(principal.IsInRole(Config.SysAdminRole));
    }

    [Theory]
    [InlineData("non-admin")]
    [InlineData("anonymous")]
    public async Task AccessDeniedPage_WithoutAReturnUrl_SendsLoginToTheConsoleRoot(string identity)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", identity);

        HttpResponseMessage response = await client.GetAsync("/Account/AccessDenied");
        string content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/Account/Login?returnUrl=%2FAdmin", content);
    }

    [Theory]
    [InlineData("non-admin")]
    [InlineData("anonymous")]
    public async Task AccessDeniedPage_WithAReturnUrl_SendsLoginBackToTheRefusedPage(string identity)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", identity);

        HttpResponseMessage response = await client.GetAsync(
            "/Account/AccessDenied?ReturnUrl=%2FAdmin%2FClients");
        string content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/Account/Login?returnUrl=%2FAdmin%2FClients", content);
    }

    [Theory]
    [InlineData("non-admin")]
    [InlineData("anonymous")]
    public async Task AccessDeniedPage_WithABlankReturnUrl_FallsBackToTheConsoleRoot(string identity)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", identity);

        HttpResponseMessage response = await client.GetAsync("/Account/AccessDenied?ReturnUrl=%20");
        string content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/Account/Login?returnUrl=%2FAdmin", content);
    }
}
