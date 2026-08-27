using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.ApiScopes;

public class ApiScopesCreateIntegrationTests : IDisposable
{
    private readonly AdminWebFactory _factory;
    private readonly HttpClient _client;

    public ApiScopesCreateIntegrationTests()
    {
        _factory = new AdminWebFactory();
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(m => m.Content(html));
    }

    private async Task<(string Token, string Cookie)> GetAntiforgeryTokenAndCookieAsync()
    {
        var getResponse = await _client.GetAsync("/Admin/ApiScopes/Create");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var doc = await GetDocumentAsync(getResponse);
        var tokenInput = doc.QuerySelector("input[name='__RequestVerificationToken']") as IHtmlInputElement;
        Assert.NotNull(tokenInput);

        var cookies = getResponse.Headers.GetValues("Set-Cookie");
        var cookie = cookies.FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery"));
        Assert.NotNull(cookie);

        return (tokenInput!.Value, cookie!);
    }

    [Fact]
    public async Task OnGet_Should_Return200OK_AndRenderCreateForm()
    {
        var response = await _client.GetAsync("/Admin/ApiScopes/Create");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await GetDocumentAsync(response);

        var title = doc.QuerySelector(".hero-title");
        Assert.NotNull(title);
        Assert.Contains("Create API Scope", title.TextContent);

        var nameInput = doc.QuerySelector("input[name='Input.Name']");
        Assert.NotNull(nameInput);

        var displayNameInput = doc.QuerySelector("input[name='Input.DisplayName']");
        Assert.NotNull(displayNameInput);

        var descriptionInput = doc.QuerySelector("textarea[name='Input.Description']");
        Assert.NotNull(descriptionInput);

        var submitBtn = doc.QuerySelector("button[type='submit'].btn-primary");
        Assert.NotNull(submitBtn);
        Assert.Contains("Create Scope", submitBtn.TextContent);
    }

    [Fact]
    public async Task OnPost_Should_CreateScope_AndRedirectToEdit_When_NameIsUnique()
    {
        var (token, cookie) = await GetAntiforgeryTokenAndCookieAsync();

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Name", "test.new.scope"),
            new KeyValuePair<string, string>("Input.DisplayName", "Test New Scope"),
            new KeyValuePair<string, string>("Input.Description", "A test scope for verification"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await _client.PostAsync("/Admin/ApiScopes/Create", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/ApiScopes/Edit", response.Headers.Location?.ToString());
        Assert.Contains("name=test.new.scope", response.Headers.Location?.ToString());

        // Verify scope was persisted
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var scope = await configDb.ApiScopes
                .FirstOrDefaultAsync(s => s.Name == "test.new.scope");

            Assert.NotNull(scope);
            Assert.Equal("Test New Scope", scope.DisplayName);
            Assert.Equal("A test scope for verification", scope.Description);
            Assert.True(scope.Enabled);
        });
    }

    [Fact]
    public async Task OnPost_Should_ShowValidationError_When_NameCollisionWithExistingApiScope()
    {
        // Pre-create a scope
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiScopes.Add(new ApiScope { Name = "existing.scope", Enabled = true });
            await configDb.SaveChangesAsync();
        });

        var (token, cookie) = await GetAntiforgeryTokenAndCookieAsync();

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Name", "existing.scope"),
            new KeyValuePair<string, string>("Input.DisplayName", "Conflicting Scope"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await _client.PostAsync("/Admin/ApiScopes/Create", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await GetDocumentAsync(response);
        // Check for error message in the page (either validation summary or error list)
        var pageText = doc.DocumentElement.TextContent;
        Assert.Contains("already exists", pageText);
    }

    [Fact]
    public async Task OnPost_Should_ShowValidationError_When_NameCollisionWithIdentityResource()
    {
        // Pre-create an identity resource
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.IdentityResources.Add(new IdentityResource { Name = "openid", Enabled = true });
            await configDb.SaveChangesAsync();
        });

        var (token, cookie) = await GetAntiforgeryTokenAndCookieAsync();

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Name", "openid"),
            new KeyValuePair<string, string>("Input.DisplayName", "OpenID Connect"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await _client.PostAsync("/Admin/ApiScopes/Create", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await GetDocumentAsync(response);
        var pageText = doc.DocumentElement.TextContent;
        Assert.Contains("already exists", pageText);
    }

    [Fact]
    public async Task OnPost_Should_ShowValidationError_When_NameIsEmpty()
    {
        var (token, cookie) = await GetAntiforgeryTokenAndCookieAsync();

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Name", ""),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await _client.PostAsync("/Admin/ApiScopes/Create", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await GetDocumentAsync(response);
        var pageText = doc.DocumentElement.TextContent;
        Assert.Contains("required", pageText, System.StringComparison.OrdinalIgnoreCase);
    }
}


