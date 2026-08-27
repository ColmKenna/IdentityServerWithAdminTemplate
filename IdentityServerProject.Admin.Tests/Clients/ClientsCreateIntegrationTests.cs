using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Duende.IdentityServer.Models;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientsCreateIntegrationTests : IDisposable
{
    private readonly AdminWebFactory _factory;
    private readonly HttpClient _client;

    public ClientsCreateIntegrationTests()
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
        var getResponse = await _client.GetAsync("/Admin/Clients/Create");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var doc = await GetDocumentAsync(getResponse);
        var tokenInput = doc.QuerySelector("input[name='__RequestVerificationToken']") as IHtmlInputElement;
        Assert.NotNull(tokenInput);

        var cookies = getResponse.Headers.GetValues("Set-Cookie");
        var cookie = cookies.FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery"));
        Assert.NotNull(cookie);

        return (tokenInput!.Value, cookie!);
    }

    private async Task SeedIdentityScopesAsync()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            if (!await db.IdentityResources.AnyAsync(resource => resource.Name == "openid"))
            {
                db.IdentityResources.Add(new IdentityResource("openid", new[] { "sub" }).ToEntity());
                db.IdentityResources.Add(new IdentityResource("profile", new[] { "name" }).ToEntity());
                await db.SaveChangesAsync();
            }
        });
    }

    [Fact]
    public async Task OnGet_Should_Return200OK_AndRenderCreateFormWithPresetCards()
    {
        var response = await _client.GetAsync("/Admin/Clients/Create");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await GetDocumentAsync(response);

        var title = doc.QuerySelector(".page-title");
        Assert.NotNull(title);
        Assert.Contains("Register New Client", title.TextContent);

        // Verify preset cards exist
        var presetCards = doc.QuerySelectorAll(".preset-card");
        Assert.Equal(4, presetCards.Length);

        // Verify required form elements
        var clientIdInput = doc.QuerySelector("input[name='Input.ClientId']");
        var clientNameInput = doc.QuerySelector("input[name='Input.ClientName']");
        var submitBtn = doc.QuerySelector("button[type='submit'].btn-primary");

        Assert.NotNull(clientIdInput);
        Assert.NotNull(clientNameInput);
        Assert.NotNull(submitBtn);
    }

    [Fact]
    public async Task OnPost_Should_CreateClient_AndDisplaySecretRevealBanner()
    {
        await SeedIdentityScopesAsync();
        var (token, cookie) = await GetAntiforgeryTokenAndCookieAsync();

        var clientId = $"web-app-{Guid.NewGuid():N}";
        var formData = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Input.ClientId", clientId },
            { "Input.ClientName", "Integration Web App" },
            { "Input.Description", "Created via Integration Test" },
            { "Input.SelectedPreset", "web" },
            { "Input.RequirePkce", "true" },
            { "Input.RequireClientSecret", "true" },
            { "Input.GrantTypes", "authorization_code" },
            { "Input.RedirectUris", "https://localhost:5001/signin-oidc" },
            { "Input.AllowedScopes", "openid" }
        };

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Create");
        postRequest.Headers.Add("Cookie", cookie);
        postRequest.Content = new FormUrlEncodedContent(formData);

        var postResponse = await _client.SendAsync(postRequest);

        // Razor Pages PRG keeps the opaque handle in TempData, never in the URL.
        Assert.Equal(HttpStatusCode.Redirect, postResponse.StatusCode);
        Assert.Equal($"/Admin/Clients/Create?clientId={clientId}", postResponse.Headers.Location?.OriginalString);
        Assert.DoesNotContain("token", postResponse.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);

        // Preserve cookies when following redirect to GET page with TempData reveal mode
        var getRequest = new HttpRequestMessage(HttpMethod.Get, postResponse.Headers.Location);
        var responseCookies = postResponse.Headers.Contains("Set-Cookie")
            ? string.Join("; ", postResponse.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]))
            : cookie.Split(';')[0];
        getRequest.Headers.Add("Cookie", responseCookies);

        var getResponse = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var doc = await GetDocumentAsync(getResponse);

        var secretBanner = doc.QuerySelector("#secret-reveal-banner");
        Assert.NotNull(secretBanner);
        Assert.Contains("Client Secret Generated", secretBanner.TextContent);
        Assert.Contains(clientId, secretBanner.TextContent);

        var secretInput = doc.QuerySelector("#generated-secret-input") as IHtmlInputElement;
        Assert.NotNull(secretInput);
        Assert.False(string.IsNullOrWhiteSpace(secretInput.Value));

        // Verify saved client in database
        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            var client = await db.Clients
                .Include(c => c.ClientSecrets)
                .FirstOrDefaultAsync(c => c.ClientId == clientId);

            Assert.NotNull(client);
            Assert.Equal("Integration Web App", client.ClientName);
            Assert.Single(client.ClientSecrets);
            // Secret in DB must be hashed, not matching the plaintext reveal
            Assert.NotEqual(secretInput.Value, client.ClientSecrets.First().Value);
        });

        var refreshRequest = new HttpRequestMessage(HttpMethod.Get, postResponse.Headers.Location);
        refreshRequest.Headers.Add("Cookie", responseCookies);
        var refreshResponse = await _client.SendAsync(refreshRequest);
        var refreshDocument = await GetDocumentAsync(refreshResponse);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.Null(refreshDocument.QuerySelector("#secret-reveal-banner"));
    }

    [Fact]
    public async Task OnPost_Should_ShowValidationError_When_ClientIdIsMissing()
    {
        var (token, cookie) = await GetAntiforgeryTokenAndCookieAsync();

        var formData = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Input.ClientId", "" },
            { "Input.ClientName", "Invalid Client" },
            { "Input.SelectedPreset", "web" }
        };

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Create");
        postRequest.Headers.Add("Cookie", cookie);
        postRequest.Content = new FormUrlEncodedContent(formData);

        var response = await _client.SendAsync(postRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await GetDocumentAsync(response);
        var validationSummary = doc.QuerySelector(".validation-summary");
        Assert.NotNull(validationSummary);
        Assert.Contains("Client ID is required", validationSummary.TextContent);
    }

    [Fact]
    public async Task OnPost_OverlongCollectionValues_ReturnsFieldErrorsAndDoesNotCreateClient()
    {
        var (token, cookie) = await GetAntiforgeryTokenAndCookieAsync();
        var clientId = $"overlong-http-{Guid.NewGuid():N}";
        var formData = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Input.ClientId", clientId },
            { "Input.ClientName", "Overlong HTTP Client" },
            { "Input.SelectedPreset", "web" },
            { "Input.GrantTypes", new string('g', ValidationConstants.MaxGrantTypeLength + 1) },
            { "Input.RedirectUris", "https://example.com/" + new string('r', ValidationConstants.MaxClientRedirectUriLength) },
            { "Input.PostLogoutRedirectUris", "https://example.com/" + new string('p', ValidationConstants.MaxClientPostLogoutRedirectUriLength) },
            { "Input.CorsOrigins", "https://example.com/" + new string('c', ValidationConstants.MaxClientCorsOriginLength) }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Create");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(formData);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await GetDocumentAsync(response);
        var summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);
        Assert.Contains("Grant types cannot exceed", summary!.TextContent);
        Assert.Contains("absolute HTTP or HTTPS URL", summary.TextContent);

        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ConfigurationDbContext>();
            Assert.False(await db.Clients.AsNoTracking().AnyAsync(client => client.ClientId == clientId));
        });
    }
}


