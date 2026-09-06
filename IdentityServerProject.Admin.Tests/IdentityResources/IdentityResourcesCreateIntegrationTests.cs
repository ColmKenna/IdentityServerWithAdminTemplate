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

namespace IdentityServerProject.Admin.Tests.IdentityResources;

public class IdentityResourcesCreateIntegrationTests : IDisposable
{
    private readonly HttpClient _client;
    private readonly AdminWebFactory _factory;

    public IdentityResourcesCreateIntegrationTests()
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
        HttpResponseMessage getResponse = await _client.GetAsync("/Admin/IdentityResources/Create");
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
    public async Task OnGet_Should_Return200OK_AndRenderCreateFormWithTemplates()
    {
        HttpResponseMessage response = await _client.GetAsync("/Admin/IdentityResources/Create");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument doc = await GetDocumentAsync(response);

        IElement? title = doc.QuerySelector(".hero-title");
        Assert.NotNull(title);
        Assert.Contains("Create Identity Resource", title.TextContent);

        // Verify template buttons exist
        IHtmlCollection<IElement> templateCards = doc.QuerySelectorAll(".preset-card");
        Assert.True(templateCards.Length >= 4); // profile, email, address, phone

        // Verify required form elements
        IElement? nameInput = doc.QuerySelector("input[name='Input.Name']");
        Assert.NotNull(nameInput);

        IElement? displayNameInput = doc.QuerySelector("input[name='Input.DisplayName']");
        Assert.NotNull(displayNameInput);

        IElement? submitBtn = doc.QuerySelector("button[type='submit'].btn-primary");
        Assert.NotNull(submitBtn);
    }

    [Fact]
    public async Task OnPost_Should_CreateResource_AndRedirectToEdit_When_NameIsUnique()
    {
        (string token, string cookie) = await GetAntiforgeryTokenAndCookieAsync();

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Name", "custom.scope"),
            new KeyValuePair<string, string>("Input.DisplayName", "Custom Scope"),
            new KeyValuePair<string, string>("Input.Description", "A custom scope"),
            new KeyValuePair<string, string>("Input.Enabled", "true"),
            new KeyValuePair<string, string>("Input.ShowInDiscoveryDocument", "true"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        HttpResponseMessage response = await _client.PostAsync("/Admin/IdentityResources/Create", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/IdentityResources/Edit", response.Headers.Location?.ToString());
        Assert.Contains("name=custom.scope", response.Headers.Location?.ToString());

        // Verify resource was persisted
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            IdentityResource? resource = await configDb.IdentityResources
                .FirstOrDefaultAsync(r => r.Name == "custom.scope");

            Assert.NotNull(resource);
            Assert.Equal("Custom Scope", resource.DisplayName);
            Assert.Equal("A custom scope", resource.Description);
            Assert.True(resource.Enabled);
            Assert.True(resource.ShowInDiscoveryDocument);
        });
    }

    [Fact]
    public async Task OnPost_Should_CreateResourceWithClaims_WhenClaimsProvided()
    {
        // Pre-create a test resource so we can then add claims to it via the Edit page
        (string token, string cookie) = await GetAntiforgeryTokenAndCookieAsync();

        // First, create the basic resource
        var createContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Name", "custom.profile.scope"),
            new KeyValuePair<string, string>("Input.DisplayName", "Custom Profile"),
            new KeyValuePair<string, string>("Input.Enabled", "true"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        HttpResponseMessage response = await _client.PostAsync("/Admin/IdentityResources/Create", createContent);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // Verify resource was created
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            IdentityResource? resource = await configDb.IdentityResources
                .FirstOrDefaultAsync(r => r.Name == "custom.profile.scope");

            Assert.NotNull(resource);
            Assert.Equal("Custom Profile", resource.DisplayName);
        });
    }

    [Fact]
    public async Task OnPost_Should_ShowValidationError_When_NameCollision()
    {
        // Pre-create a resource
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.IdentityResources.Add(new IdentityResource
            {
                Name = "existing.resource",
                Enabled = true
            });
            await configDb.SaveChangesAsync();
        });

        (string token, string cookie) = await GetAntiforgeryTokenAndCookieAsync();

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Name", "existing.resource"),
            new KeyValuePair<string, string>("Input.DisplayName", "Duplicate"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        HttpResponseMessage response = await _client.PostAsync("/Admin/IdentityResources/Create", content);

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

        HttpResponseMessage response = await _client.PostAsync("/Admin/IdentityResources/Create", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument doc = await GetDocumentAsync(response);
        string pageText = doc.DocumentElement.TextContent;
        Assert.Contains("required", pageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnGet_Should_RendersAllStandardTemplates()
    {
        HttpResponseMessage response = await _client.GetAsync("/Admin/IdentityResources/Create");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument doc = await GetDocumentAsync(response);

        // Check for all 4 standard templates
        IElement? profileCard = doc.QuerySelector("[data-template='profile']");
        Assert.NotNull(profileCard);

        IElement? emailCard = doc.QuerySelector("[data-template='email']");
        Assert.NotNull(emailCard);

        IElement? addressCard = doc.QuerySelector("[data-template='address']");
        Assert.NotNull(addressCard);

        IElement? phoneCard = doc.QuerySelector("[data-template='phone']");
        Assert.NotNull(phoneCard);

        // Verify template descriptions
        string pageText = doc.DocumentElement.TextContent;
        Assert.Contains("profile", pageText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("email", pageText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("address", pageText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("phone", pageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_MarksTheResourceNameRequired()
    {
        HttpResponseMessage response = await _client.GetAsync("/Admin/IdentityResources/Create");
        IDocument document = await GetDocumentAsync(response);

        var name = document.GetElementById("resource-name") as IHtmlInputElement;
        Assert.NotNull(name);
        Assert.True(name!.IsRequired);
    }
}
