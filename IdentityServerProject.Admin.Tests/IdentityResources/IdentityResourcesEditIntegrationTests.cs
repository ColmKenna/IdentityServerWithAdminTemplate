using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.IdentityResources;

public class IdentityResourcesEditIntegrationTests : IDisposable
{
    private readonly AdminWebFactory _factory;
    private readonly HttpClient _client;

    public IdentityResourcesEditIntegrationTests()
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

    private async Task<(string Token, string Cookie)> GetAntiforgeryTokenAndCookieAsync(string resourceName)
    {
        var getResponse = await _client.GetAsync($"/Admin/IdentityResources/Edit?name={resourceName}");
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
    public async Task OnGet_Should_Return200OK_AndRenderEditFormWithClaims()
    {
        // Pre-create a test resource
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var resource = new IdentityResource
            {
                Name = "test.resource",
                DisplayName = "Test Resource",
                Description = "A test resource",
                Enabled = true,
                UserClaims = new List<IdentityResourceClaim>
                {
                    new() { Type = "email" },
                    new() { Type = "phone_number" },
                }
            };
            configDb.IdentityResources.Add(resource);
            await configDb.SaveChangesAsync();
        });

        var response = await _client.GetAsync("/Admin/IdentityResources/Edit?name=test.resource");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await GetDocumentAsync(response);

        var title = doc.QuerySelector(".hero-title");
        Assert.NotNull(title);
        Assert.Contains("test.resource", title.TextContent);

        var displayNameInput = doc.QuerySelector("input[name='Input.DisplayName']");
        Assert.NotNull(displayNameInput);
        Assert.Equal("Test Resource", (displayNameInput as IHtmlInputElement)?.Value);

        var claimChips = doc.QuerySelectorAll(".scope-chip");
        Assert.NotEmpty(claimChips);
    }

    [Fact]
    public async Task OnPost_Should_UpdateBasics_WhenFormIsValid()
    {
        // Pre-create a test resource
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.IdentityResources.Add(new IdentityResource
            {
                Name = "test.update",
                DisplayName = "Original Name",
                Enabled = true,
            });
            await configDb.SaveChangesAsync();
        });

        var (token, cookie) = await GetAntiforgeryTokenAndCookieAsync("test.update");

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.DisplayName", "Updated Name"),
            new KeyValuePair<string, string>("Input.Description", "Updated description"),
            new KeyValuePair<string, string>("Input.Enabled", "true"),
            new KeyValuePair<string, string>("Input.Required", "false"),
            new KeyValuePair<string, string>("Input.Emphasize", "true"),
            new KeyValuePair<string, string>("Input.ShowInDiscoveryDocument", "true"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await _client.PostAsync("/Admin/IdentityResources/Edit?name=test.update&handler=Save", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin/IdentityResources/Edit", response.Headers.Location?.ToString());

        // Verify changes persisted
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var resource = await configDb.IdentityResources
                .FirstOrDefaultAsync(r => r.Name == "test.update");

            Assert.NotNull(resource);
            Assert.Equal("Updated Name", resource.DisplayName);
            Assert.Equal("Updated description", resource.Description);
            Assert.True(resource.Emphasize);
        });
    }

    [Fact]
    public async Task OnPostAddClaim_Should_ProtectOpenIdClaim()
    {
        // Pre-create a test resource
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.IdentityResources.Add(new IdentityResource
            {
                Name = "test.openid.protection",
                Enabled = true,
            });
            await configDb.SaveChangesAsync();
        });

        var (token, cookie) = await GetAntiforgeryTokenAndCookieAsync("test.openid.protection");

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("claimType", "openid"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await _client.PostAsync("/Admin/IdentityResources/Edit?name=test.openid.protection&handler=AddClaim", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await GetDocumentAsync(response);
        var pageText = doc.DocumentElement.TextContent;
        Assert.Contains("openid", pageText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("protected", pageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnPostAddClaim_Should_AddNonOpenIdClaim()
    {
        // Pre-create a test resource
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.IdentityResources.Add(new IdentityResource
            {
                Name = "test.add.claim",
                Enabled = true,
            });
            await configDb.SaveChangesAsync();
        });

        var (token, cookie) = await GetAntiforgeryTokenAndCookieAsync("test.add.claim");

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("claimType", "email"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await _client.PostAsync("/Admin/IdentityResources/Edit?name=test.add.claim&handler=AddClaim", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // Verify claim was added
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var resource = await configDb.IdentityResources
                .Include(r => r.UserClaims)
                .FirstOrDefaultAsync(r => r.Name == "test.add.claim");

            Assert.NotNull(resource);
            Assert.Contains(resource.UserClaims, c => c.Type == "email");
        });
    }

    [Fact]
    public async Task OnPostRemoveClaim_Should_ProtectOpenIdClaim()
    {
        // Pre-create a test resource with openid claim
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var resource = new IdentityResource
            {
                Name = "test.openid.removal",
                Enabled = true,
                UserClaims = new List<IdentityResourceClaim>
                {
                    new() { Type = "openid" },
                }
            };
            configDb.IdentityResources.Add(resource);
            await configDb.SaveChangesAsync();
        });

        var (token, cookie) = await GetAntiforgeryTokenAndCookieAsync("test.openid.removal");

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("claimType", "openid"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await _client.PostAsync("/Admin/IdentityResources/Edit?name=test.openid.removal&handler=RemoveClaim", content);

        // A refusal is reported as a rendered reason, not a 404. Reporting a guarded resource as
        // missing tells the operator the wrong thing and hides the guard behind a broken link.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await GetDocumentAsync(response);
        var pageText = doc.DocumentElement.TextContent;
        Assert.Contains("openid", pageText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("protected", pageText, StringComparison.OrdinalIgnoreCase);

        // Verify openid claim is still present
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var resource = await configDb.IdentityResources
                .Include(r => r.UserClaims)
                .FirstOrDefaultAsync(r => r.Name == "test.openid.removal");

            Assert.NotNull(resource);
            Assert.Contains(resource.UserClaims, c => c.Type == "openid");
        });
    }

    [Fact]
    public async Task OnPostRemoveClaim_Should_RemoveNonOpenIdClaim()
    {
        // Pre-create a test resource with email and phone claims
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var resource = new IdentityResource
            {
                Name = "test.remove.claim",
                Enabled = true,
                UserClaims = new List<IdentityResourceClaim>
                {
                    new() { Type = "email" },
                    new() { Type = "phone_number" },
                }
            };
            configDb.IdentityResources.Add(resource);
            await configDb.SaveChangesAsync();
        });

        var (token, cookie) = await GetAntiforgeryTokenAndCookieAsync("test.remove.claim");

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("claimType", "phone_number"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        });

        _client.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await _client.PostAsync("/Admin/IdentityResources/Edit?name=test.remove.claim&handler=RemoveClaim", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // Verify phone_number was removed but email remains
        await _factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var resource = await configDb.IdentityResources
                .Include(r => r.UserClaims)
                .FirstOrDefaultAsync(r => r.Name == "test.remove.claim");

            Assert.NotNull(resource);
            Assert.Contains(resource.UserClaims, c => c.Type == "email");
            Assert.DoesNotContain(resource.UserClaims, c => c.Type == "phone_number");
        });
    }
}

