extern alias ApiService;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ApiServiceProgram = ApiService::Program;

namespace Sales.Tests;

public class ApiAuthorizationTests : IClassFixture<WebApplicationFactory<ApiServiceProgram>>
{
    private readonly WebApplicationFactory<ApiServiceProgram> _factory;

    public ApiAuthorizationTests(WebApplicationFactory<ApiServiceProgram> factory)
    {
        // Program.cs reads these via IConfiguration during top-level statement execution, before
        // WebApplicationFactory's ConfigureAppConfiguration hook runs, so they must be supplied as
        // environment variables (ASP.NET Core's default config sources read these unprefixed).
        Environment.SetEnvironmentVariable("services__identityserver__https__0", "https://localhost:5001");
        Environment.SetEnvironmentVariable("ConnectionStrings__SalesDb", "Server=(localdb)\\NonExistent;Database=SalesDbTests;Trusted_Connection=True;");

        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task GetRoot_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetWeatherForecast_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/weatherforecast", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetWeatherForecast_WithGarbageToken_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-real-jwt");

        var response = await client.GetAsync("/weatherforecast", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminPing_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/admin/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiScopePolicy_DeniesPrincipal_WithWrongScope()
    {
        using var scope = _factory.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();

        var principal = PrincipalWithClaims(("scope", "some.other.api"));

        var result = await authService.AuthorizeAsync(principal, resource: null, policyName: "ApiScope");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ApiScopePolicy_SucceedsForPrincipal_WithCorrectScope()
    {
        using var scope = _factory.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();

        var principal = PrincipalWithClaims(("scope", "sales.api"));

        var result = await authService.AuthorizeAsync(principal, resource: null, policyName: "ApiScope");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SysAdminPolicy_DeniesPrincipal_WithCorrectScopeButNoAdminRole()
    {
        using var scope = _factory.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();

        var principal = PrincipalWithClaims(("scope", "sales.api"));

        var result = await authService.AuthorizeAsync(principal, resource: null, policyName: "SysAdmin");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SysAdminPolicy_SucceedsForPrincipal_WithScopeAndAdminRole()
    {
        using var scope = _factory.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();

        var principal = PrincipalWithClaims(("scope", "sales.api"), ("role", "SysAdmin"));

        var result = await authService.AuthorizeAsync(principal, resource: null, policyName: "SysAdmin");

        Assert.True(result.Succeeded);
    }

    private static System.Security.Claims.ClaimsPrincipal PrincipalWithClaims(params (string Type, string Value)[] claims)
    {
        var identity = new System.Security.Claims.ClaimsIdentity(
            claims.Select(c => new System.Security.Claims.Claim(c.Type, c.Value)),
            authenticationType: "TestAuth",
            nameType: "name",
            roleType: "role");

        return new System.Security.Claims.ClaimsPrincipal(identity);
    }
}
