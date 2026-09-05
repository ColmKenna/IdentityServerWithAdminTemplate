using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.ApiScopes;

public class ApiScopesCreateIntegrationTests : IDisposable
{
    private readonly HttpClient _client;
    private readonly AdminWebFactory _factory;

    public ApiScopesCreateIntegrationTests()
    {
        _factory = new AdminWebFactory();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
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
        string html = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(m => m.Content(html));
    }

    private async Task<(string Token, string Cookie)> GetAntiforgeryTokenAndCookieAsync()
    {
        HttpResponseMessage getResponse = await _client.GetAsync("/Admin/ApiScopes/Create");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        IDocument doc = await GetDocumentAsync(getResponse);
        var tokenInput = doc.QuerySelector("input[name='__RequestVerificationToken']") as IHtmlInputElement;
        Assert.NotNull(tokenInput);

        IEnumerable<string> cookies = getResponse.Headers.GetValues("Set-Cookie");
        string? cookie = cookies.FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery"));
        Assert.NotNull(cookie);

        return (tokenInput!.Value, cookie!);
    }

    [Fact]
    public async Task OnGet_Should_Return200OK_AndRenderCreateForm()
    {
        HttpResponseMessage response = await _client.GetAsync("/Admin/ApiScopes/Create");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument doc = await GetDocumentAsync(response);

        IElement? title = doc.QuerySelector(".hero-title");
        Assert.NotNull(title);
        Assert.Contains("Create API Scope", title.TextContent);

        IElement? nameInput = doc.QuerySelector("input[name='Input.Name']");
        Assert.NotNull(nameInput);

        IElement? displayNameInput = doc.QuerySelector("input[name='Input.DisplayName']");
        Assert.NotNull(displayNameInput);

        IElement? descriptionInput = doc.QuerySelector("textarea[name='Input.Description']");
        Assert.NotNull(descriptionInput);

        IElement? submitBtn = doc.QuerySelector("button[type='submit'].btn-primary");
        Assert.NotNull(submitBtn);
        Assert.Contains("Create Scope", submitBtn.TextContent);
    }

    [Fact]
    public async Task OnPost_Should_CreateScope_AndRedirectToEdit_When_NameIsUnique()
    {
        (string token, string cookie) = await GetAntiforgeryTokenAndCookieAsync();

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Name", "test.new.scope"),
            new KeyValuePair<string, string>("Input.DisplayName", "Test New Scope"),
            new KeyValuePair<string, string>("Input.Description", "A test scope for verification"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        HttpResponseMessage response = await _client.PostAsync("/Admin/ApiScopes/Create", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/ApiScopes/Edit", response.Headers.Location?.ToString());
        Assert.Contains("name=test.new.scope", response.Headers.Location?.ToString());

        // Verify scope was persisted
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            ApiScope? scope = await configDb.ApiScopes
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
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiScopes.Add(new ApiScope { Name = "existing.scope", Enabled = true });
            await configDb.SaveChangesAsync();
        });

        (string token, string cookie) = await GetAntiforgeryTokenAndCookieAsync();

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Name", "existing.scope"),
            new KeyValuePair<string, string>("Input.DisplayName", "Conflicting Scope"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        HttpResponseMessage response = await _client.PostAsync("/Admin/ApiScopes/Create", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument doc = await GetDocumentAsync(response);
        // Check for error message in the page (either validation summary or error list)
        string pageText = doc.DocumentElement.TextContent;
        Assert.Contains("already exists", pageText);
    }

    [Fact]
    public async Task OnPost_Should_ShowValidationError_When_NameCollisionWithIdentityResource()
    {
        // Pre-create an identity resource
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.IdentityResources.Add(new IdentityResource { Name = "openid", Enabled = true });
            await configDb.SaveChangesAsync();
        });

        (string token, string cookie) = await GetAntiforgeryTokenAndCookieAsync();

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Name", "openid"),
            new KeyValuePair<string, string>("Input.DisplayName", "OpenID Connect"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        HttpResponseMessage response = await _client.PostAsync("/Admin/ApiScopes/Create", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument doc = await GetDocumentAsync(response);
        string pageText = doc.DocumentElement.TextContent;
        Assert.Contains("already exists", pageText);
    }

    [Fact]
    public async Task OnPost_Should_ShowValidationError_When_NameIsEmpty()
    {
        (string token, string cookie) = await GetAntiforgeryTokenAndCookieAsync();

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Name", ""),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        HttpResponseMessage response = await _client.PostAsync("/Admin/ApiScopes/Create", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument doc = await GetDocumentAsync(response);
        string pageText = doc.DocumentElement.TextContent;
        Assert.Contains("required", pageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_MarksTheScopeNameRequired()
    {
        HttpResponseMessage response = await _client.GetAsync("/Admin/ApiScopes/Create");
        IDocument document = await GetDocumentAsync(response);

        // The visible asterisk was the only signal; the model has carried [Required] all along.
        var name = document.GetElementById("scope-name") as IHtmlInputElement;
        Assert.NotNull(name);
        Assert.True(name!.IsRequired);
    }
}
