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

/// <summary>
///     Full HTTP pipeline tests for /Admin/ApiScopes/Edit (WI-24), covering the two Scenario
///     Review points agreed at plan approval: Name field immutability and claim add/remove.
///     Uses a real (SQLite in-memory) database via <see cref="AdminWebFactory" /> so the
///     redirect/model-binding/antiforgery pipeline is exercised end to end.
/// </summary>
public class ApiScopesEditIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private (AdminWebFactory Factory, HttpClient Client) CreateClient(bool allowAutoRedirect = true)
    {
        var factory = new AdminWebFactory();
        _disposables.Add(factory);

        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });

        return (factory, client);
    }

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    private static async Task<(string Token, string Cookie)> ExtractAntiForgeryTokenAndCookieAsync(
        HttpClient httpClient, string pageUrl)
    {
        HttpResponseMessage response = await httpClient.GetAsync(pageUrl);
        IDocument document = await GetDocumentAsync(response);

        var tokenInput = document.QuerySelector("input[name='__RequestVerificationToken']") as IHtmlInputElement;
        Assert.NotNull(tokenInput);

        string token = tokenInput!.Value;
        IEnumerable<string> cookies = response.Headers.GetValues("Set-Cookie");
        string? cookie = cookies.FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery"));
        Assert.NotNull(cookie);

        return (token, cookie!);
    }

    private static async Task SeedApiScopeAsync(AdminWebFactory factory, ApiScope scope)
    {
        await factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiScopes.Add(scope);
            await configDb.SaveChangesAsync();
        });
    }

    // ---------- Name field immutability ----------

    [Fact]
    public async Task Get_ExistingScope_RendersNameInputDisabled()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient();
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-scope";
        await SeedApiScopeAsync(factory, new ApiScope { Name = name, DisplayName = "Display" });

        HttpResponseMessage response = await client.GetAsync($"/Admin/ApiScopes/Edit?name={name}");
        IDocument document = await GetDocumentAsync(response);

        var nameInput = document.QuerySelector("#scope-name") as IHtmlInputElement;
        Assert.NotNull(nameInput);
        Assert.True(nameInput!.IsDisabled);
        Assert.Equal(name, nameInput.Value);
    }

    [Fact]
    public async Task Get_UnknownScopeName_Returns404()
    {
        (_, HttpClient client) = CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/Admin/ApiScopes/Edit?name={Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostSave_ValidInput_UpdatesDisplayNameAndDescription()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient(false);
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-scope";
        await SeedApiScopeAsync(factory, new ApiScope { Name = name, DisplayName = "Old", Description = "Old desc" });

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/ApiScopes/Edit?name={name}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/ApiScopes/Edit?name={name}&handler=Save");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.DisplayName"] = "New Display",
            ["Input.Description"] = "New Desc"
        });

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            ApiScope scope = await configDb.ApiScopes.SingleAsync(s => s.Name == name);
            Assert.Equal("New Display", scope.DisplayName);
            Assert.Equal("New Desc", scope.Description);
        });
    }

    [Fact]
    public async Task PostSave_SpoofedNameFieldInBody_UpdatesOnlyTheScopeNamedInTheUrlAndLeavesOtherScopeUntouched()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient(false);
        string tag = Guid.NewGuid().ToString("N");
        string targetName = $"{tag}-target";
        string otherName = $"{tag}-other";
        await SeedApiScopeAsync(factory, new ApiScope { Name = targetName, DisplayName = "Target Old" });
        await SeedApiScopeAsync(factory, new ApiScope { Name = otherName, DisplayName = "Other Old" });

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/ApiScopes/Edit?name={targetName}");

        // The URL identifies targetName; a malicious/tampered body tries to redirect the
        // update onto otherName via a spoofed "name" field. Name binds from the query
        // string only ([FromQuery]), so this must have no effect.
        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/ApiScopes/Edit?name={targetName}&handler=Save");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = otherName,
            ["Input.DisplayName"] = "Hijacked"
        });

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"name={targetName}", response.Headers.Location!.OriginalString);
        await factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            ApiScope target = await configDb.ApiScopes.SingleAsync(s => s.Name == targetName);
            ApiScope other = await configDb.ApiScopes.SingleAsync(s => s.Name == otherName);
            Assert.Equal("Hijacked", target.DisplayName);
            Assert.Equal("Other Old", other.DisplayName);
        });
    }

    [Fact]
    public async Task PostSave_ScopeDoesNotExist_Returns404()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient(false);
        string tag = Guid.NewGuid().ToString("N");
        string existingName = $"{tag}-existing";
        string missingName = $"{tag}-missing";
        await SeedApiScopeAsync(factory, new ApiScope { Name = existingName });

        // The editor GET for a non-existent name 404s too, so the antiforgery token/cookie is
        // pulled from a real scope's editor page instead (tokens aren't tied to route data).
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/ApiScopes/Edit?name={existingName}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/ApiScopes/Edit?name={missingName}&handler=Save");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Claim management ----------

    [Fact]
    public async Task Get_ScopeWithClaims_RendersClaimChips()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient();
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-scope";
        await SeedApiScopeAsync(factory,
            new ApiScope { Name = name, UserClaims = new List<ApiScopeClaim> { new() { Type = "email" } } });

        HttpResponseMessage response = await client.GetAsync($"/Admin/ApiScopes/Edit?name={name}");
        IDocument document = await GetDocumentAsync(response);

        IElement? chip = document.QuerySelector(".scope-chip");
        Assert.NotNull(chip);
        Assert.Equal("email", chip!.TextContent.Trim());
    }

    [Fact]
    public async Task PostAddClaim_NewClaimType_PersistsAndRendersOnReload()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient();
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-scope";
        await SeedApiScopeAsync(factory, new ApiScope { Name = name });

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/ApiScopes/Edit?name={name}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/ApiScopes/Edit?name={name}&handler=AddClaim");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["claimType"] = "sub"
        });

        HttpResponseMessage response = await client.SendAsync(request);
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IElement? chip = document.QuerySelector(".scope-chip");
        Assert.NotNull(chip);
        Assert.Equal("sub", chip!.TextContent.Trim());
    }

    [Fact]
    public async Task PostRemoveClaim_ExistingClaimType_RemovesAndReflectsOnReload()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient();
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-scope";
        await SeedApiScopeAsync(factory,
            new ApiScope { Name = name, UserClaims = new List<ApiScopeClaim> { new() { Type = "sub" } } });

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/ApiScopes/Edit?name={name}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/ApiScopes/Edit?name={name}&handler=RemoveClaim");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["claimType"] = "sub"
        });

        HttpResponseMessage response = await client.SendAsync(request);
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(document.QuerySelector(".scope-chip"));
        Assert.NotNull(document.QuerySelector(".empty-state"));
    }
}