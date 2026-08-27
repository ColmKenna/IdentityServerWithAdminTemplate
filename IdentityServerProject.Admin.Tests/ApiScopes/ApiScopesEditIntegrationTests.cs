using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.ApiScopes;

/// <summary>
/// Full HTTP pipeline tests for /Admin/ApiScopes/Edit (WI-24), covering the two Scenario
/// Review points agreed at plan approval: Name field immutability and claim add/remove.
/// Uses a real (SQLite in-memory) database via <see cref="AdminWebFactory"/> so the
/// redirect/model-binding/antiforgery pipeline is exercised end to end.
/// </summary>
public class ApiScopesEditIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    private (AdminWebFactory Factory, HttpClient Client) CreateClient(bool allowAutoRedirect = true)
    {
        var factory = new AdminWebFactory();
        _disposables.Add(factory);

        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });

        return (factory, client);
    }

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    private static async Task<(string Token, string Cookie)> ExtractAntiForgeryTokenAndCookieAsync(HttpClient httpClient, string pageUrl)
    {
        var response = await httpClient.GetAsync(pageUrl);
        var document = await GetDocumentAsync(response);

        var tokenInput = document.QuerySelector("input[name='__RequestVerificationToken']") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(tokenInput);

        var token = tokenInput!.Value;
        var cookies = response.Headers.GetValues("Set-Cookie");
        var cookie = cookies.FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery"));
        Assert.NotNull(cookie);

        return (token, cookie!);
    }

    private static async Task SeedApiScopeAsync(AdminWebFactory factory, ApiScope scope)
    {
        await factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiScopes.Add(scope);
            await configDb.SaveChangesAsync();
        });
    }

    // ---------- Name field immutability ----------

    [Fact]
    public async Task Get_ExistingScope_RendersNameInputDisabled()
    {
        var (factory, client) = CreateClient();
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-scope";
        await SeedApiScopeAsync(factory, new ApiScope { Name = name, DisplayName = "Display" });

        var response = await client.GetAsync($"/Admin/ApiScopes/Edit?name={name}");
        var document = await GetDocumentAsync(response);

        var nameInput = document.QuerySelector("#scope-name") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(nameInput);
        Assert.True(nameInput!.IsDisabled);
        Assert.Equal(name, nameInput.Value);
    }

    [Fact]
    public async Task Get_UnknownScopeName_Returns404()
    {
        var (_, client) = CreateClient();

        var response = await client.GetAsync($"/Admin/ApiScopes/Edit?name={Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostSave_ValidInput_UpdatesDisplayNameAndDescription()
    {
        var (factory, client) = CreateClient(allowAutoRedirect: false);
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-scope";
        await SeedApiScopeAsync(factory, new ApiScope { Name = name, DisplayName = "Old", Description = "Old desc" });

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/ApiScopes/Edit?name={name}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/ApiScopes/Edit?name={name}&handler=Save");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.DisplayName"] = "New Display",
            ["Input.Description"] = "New Desc",
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var scope = await configDb.ApiScopes.SingleAsync(s => s.Name == name);
            Assert.Equal("New Display", scope.DisplayName);
            Assert.Equal("New Desc", scope.Description);
        });
    }

    [Fact]
    public async Task PostSave_SpoofedNameFieldInBody_UpdatesOnlyTheScopeNamedInTheUrlAndLeavesOtherScopeUntouched()
    {
        var (factory, client) = CreateClient(allowAutoRedirect: false);
        var tag = Guid.NewGuid().ToString("N");
        var targetName = $"{tag}-target";
        var otherName = $"{tag}-other";
        await SeedApiScopeAsync(factory, new ApiScope { Name = targetName, DisplayName = "Target Old" });
        await SeedApiScopeAsync(factory, new ApiScope { Name = otherName, DisplayName = "Other Old" });

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/ApiScopes/Edit?name={targetName}");

        // The URL identifies targetName; a malicious/tampered body tries to redirect the
        // update onto otherName via a spoofed "name" field. Name binds from the query
        // string only ([FromQuery]), so this must have no effect.
        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/ApiScopes/Edit?name={targetName}&handler=Save");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = otherName,
            ["Input.DisplayName"] = "Hijacked",
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"name={targetName}", response.Headers.Location!.OriginalString);
        await factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            var target = await configDb.ApiScopes.SingleAsync(s => s.Name == targetName);
            var other = await configDb.ApiScopes.SingleAsync(s => s.Name == otherName);
            Assert.Equal("Hijacked", target.DisplayName);
            Assert.Equal("Other Old", other.DisplayName);
        });
    }

    [Fact]
    public async Task PostSave_ScopeDoesNotExist_Returns404()
    {
        var (factory, client) = CreateClient(allowAutoRedirect: false);
        var tag = Guid.NewGuid().ToString("N");
        var existingName = $"{tag}-existing";
        var missingName = $"{tag}-missing";
        await SeedApiScopeAsync(factory, new ApiScope { Name = existingName });

        // The editor GET for a non-existent name 404s too, so the antiforgery token/cookie is
        // pulled from a real scope's editor page instead (tokens aren't tied to route data).
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/ApiScopes/Edit?name={existingName}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/ApiScopes/Edit?name={missingName}&handler=Save");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Claim management ----------

    [Fact]
    public async Task Get_ScopeWithClaims_RendersClaimChips()
    {
        var (factory, client) = CreateClient();
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-scope";
        await SeedApiScopeAsync(factory, new ApiScope { Name = name, UserClaims = new List<ApiScopeClaim> { new ApiScopeClaim { Type = "email" } } });

        var response = await client.GetAsync($"/Admin/ApiScopes/Edit?name={name}");
        var document = await GetDocumentAsync(response);

        var chip = document.QuerySelector(".scope-chip");
        Assert.NotNull(chip);
        Assert.Equal("email", chip!.TextContent.Trim());
    }

    [Fact]
    public async Task PostAddClaim_NewClaimType_PersistsAndRendersOnReload()
    {
        var (factory, client) = CreateClient(allowAutoRedirect: true);
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-scope";
        await SeedApiScopeAsync(factory, new ApiScope { Name = name });

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/ApiScopes/Edit?name={name}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/ApiScopes/Edit?name={name}&handler=AddClaim");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["claimType"] = "sub",
        });

        var response = await client.SendAsync(request);
        var document = await GetDocumentAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var chip = document.QuerySelector(".scope-chip");
        Assert.NotNull(chip);
        Assert.Equal("sub", chip!.TextContent.Trim());
    }

    [Fact]
    public async Task PostRemoveClaim_ExistingClaimType_RemovesAndReflectsOnReload()
    {
        var (factory, client) = CreateClient(allowAutoRedirect: true);
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-scope";
        await SeedApiScopeAsync(factory, new ApiScope { Name = name, UserClaims = new List<ApiScopeClaim> { new ApiScopeClaim { Type = "sub" } } });

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/ApiScopes/Edit?name={name}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/ApiScopes/Edit?name={name}&handler=RemoveClaim");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["claimType"] = "sub",
        });

        var response = await client.SendAsync(request);
        var document = await GetDocumentAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(document.QuerySelector(".scope-chip"));
        Assert.NotNull(document.QuerySelector(".empty-state"));
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}


