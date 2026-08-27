extern alias ApiService;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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
        Environment.SetEnvironmentVariable("ConnectionStrings__SalesDb",
            "Server=(localdb)\\NonExistent;Database=SalesDbTests;Trusted_Connection=True;");

        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task GetRoot_Returns200()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetWeatherForecast_WithoutToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/weatherforecast", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetWeatherForecast_WithGarbageToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-real-jwt");

        HttpResponseMessage response = await client.GetAsync("/weatherforecast", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminPing_WithoutToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiScopePolicy_DeniesPrincipal_WithWrongScope()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IAuthorizationService authService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        ClaimsPrincipal principal = PrincipalWithClaims(("scope", "some.other.api"));

        AuthorizationResult result = await authService.AuthorizeAsync(principal, null, "ApiScope");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ApiScopePolicy_SucceedsForPrincipal_WithCorrectScope()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IAuthorizationService authService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        ClaimsPrincipal principal = PrincipalWithClaims(("scope", "sales.api"));

        AuthorizationResult result = await authService.AuthorizeAsync(principal, null, "ApiScope");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SysAdminPolicy_DeniesPrincipal_WithCorrectScopeButNoAdminRole()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IAuthorizationService authService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        ClaimsPrincipal principal = PrincipalWithClaims(("scope", "sales.api"));

        AuthorizationResult result = await authService.AuthorizeAsync(principal, null, "SysAdmin");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SysAdminPolicy_SucceedsForPrincipal_WithScopeAndAdminRole()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IAuthorizationService authService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        ClaimsPrincipal principal = PrincipalWithClaims(("scope", "sales.api"), ("role", "SysAdmin"));

        AuthorizationResult result = await authService.AuthorizeAsync(principal, null, "SysAdmin");

        Assert.True(result.Succeeded);
    }

    private static ClaimsPrincipal PrincipalWithClaims(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            "TestAuth",
            "name",
            "role");

        return new ClaimsPrincipal(identity);
    }
}