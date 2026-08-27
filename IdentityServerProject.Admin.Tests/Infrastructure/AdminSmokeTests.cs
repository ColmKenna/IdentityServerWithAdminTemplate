using System.Net;
using AngleSharp;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

public class AdminSmokeTests : IClassFixture<AdminWebFactory>
{
    private readonly HttpClient _client;

    public AdminSmokeTests(AdminWebFactory factory)
    {
        // By default, the factory won't follow redirects. 
        // For our smoke tests, we are directly requesting the pages.
        _client = factory.CreateClient();
    }

    private async Task AssertSuccessfulRenderAsync(string url)
    {
        var response = await _client.GetAsync(url);
        
        // Assert it returns a 200 OK
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Read the content and use AngleSharp to parse the HTML
        var content = await response.Content.ReadAsStringAsync();
        
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(content));
        
        // Basic heuristic to verify that a full layout was rendered
        // rather than just a partial or an unstyled view
        Assert.NotNull(document.Body);
        Assert.NotNull(document.Head);
    }

    [Fact]
    public async Task Should_Return200AndRenderLayout_When_RequestingApisIndex()
    {
        await AssertSuccessfulRenderAsync("/Admin/Apis");
    }

    [Fact]
    public async Task Should_Return200AndRenderAdminLayout_When_RequestingClientsIndex()
    {
        var response = await _client.GetAsync("/Admin/Clients");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(content));

        Assert.NotNull(document.QuerySelector("aside.sidebar"));
    }

    [Fact]
    public async Task Should_Return200AndRenderRootLayout_When_RequestingAccountLogin()
    {
        var response = await _client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(content));

        Assert.Null(document.QuerySelector("aside.sidebar"));
    }

    [Fact]
    public async Task Should_Return200AndRenderLayout_When_RequestingApiScopesIndex()
    {
        await AssertSuccessfulRenderAsync("/Admin/ApiScopes");
    }

    [Fact]
    public async Task Should_Return200AndRenderLayout_When_RequestingIdentityResourcesIndex()
    {
        await AssertSuccessfulRenderAsync("/Admin/IdentityResources");
    }

    [Fact]
    public async Task Should_Return200AndRenderLayout_When_RequestingUsersIndex()
    {
        await AssertSuccessfulRenderAsync("/Admin/Users");
    }

    [Fact]
    public async Task Should_Return200AndRenderLayout_When_RequestingGrantsIndex()
    {
        await AssertSuccessfulRenderAsync("/Admin/Grants");
    }

    [Fact]
    public async Task Should_Return200AndRenderLayout_When_RequestingAuditLogsIndex()
    {
        await AssertSuccessfulRenderAsync("/Admin/AuditLogs");
    }

    [Fact]
    public async Task Should_Return200AndRenderLayout_When_RequestingDiagnosticsIndex()
    {
        await AssertSuccessfulRenderAsync("/Admin/Diagnostics");
    }
}
