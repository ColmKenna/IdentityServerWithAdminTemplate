using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Apis;
using IdentityServerProject.Services.Scopes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Apis;

/// <summary>
///     Full HTTP pipeline tests for /Admin/Apis/Editor (WI-14), covering the three Scenario
///     Review points agreed at plan approval: tab switching via query string, the one-time
///     secret generation banner, and the server-side contract behind the type-to-confirm
///     delete modal. Uses a real (SQLite in-memory) database via <see cref="AdminWebFactory" />
///     so the redirect/TempData/antiforgery pipeline is exercised end to end.
/// </summary>
public class ApisEditorIntegrationTests : IDisposable
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

    private static async Task SeedApiResourceAsync(AdminWebFactory factory, ApiResource resource)
    {
        await factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiResources.Add(resource);
            await configDb.SaveChangesAsync();
        });
    }

    // ---------- Tab switching via query string ----------

    [Fact]
    public async Task Get_NoTabQueryString_DefaultsToBasicsTabActive()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient();
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });

        HttpResponseMessage response = await client.GetAsync($"/Admin/Apis/Editor?name={name}");
        IDocument document = await GetDocumentAsync(response);

        IElement? activeTab = document.QuerySelector("#api-resource-editor-tabs > ck-tab[active]");
        Assert.NotNull(activeTab);
        Assert.Equal("Basics", activeTab!.GetAttribute("label"));
    }

    [Fact]
    public async Task Get_TabQueryStringSecrets_MarksSecretsTabActiveAndRendersSecretsPanel()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient();
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });

        HttpResponseMessage response = await client.GetAsync($"/Admin/Apis/Editor?name={name}&tab=secrets");
        IDocument document = await GetDocumentAsync(response);

        IElement? activeTab = document.QuerySelector("#api-resource-editor-tabs > ck-tab[active]");
        Assert.NotNull(activeTab);
        Assert.Equal("Secrets", activeTab!.GetAttribute("label"));
        Assert.NotNull(document.QuerySelector(".editor-secrets-table"));
    }

    [Fact]
    public async Task Get_NewResourceCreateMode_RendersOnlyTheUsableBasicsTab()
    {
        (_, HttpClient client) = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/Admin/Apis/Editor");
        IDocument document = await GetDocumentAsync(response);

        IHtmlCollection<IElement> tabs = document.QuerySelectorAll("#api-resource-editor-tabs > ck-tab");
        Assert.Single(tabs);
        Assert.Equal("Basics", tabs[0].GetAttribute("label"));
    }

    [Fact]
    public async Task Get_ExistingResource_RendersCkTabsAndAllNamedHandlerForms()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient();
        string name = $"{Guid.NewGuid():N}-api";
        string attachedScopeName = $"{Guid.NewGuid():N}-attached";
        string attachableScopeName = $"{Guid.NewGuid():N}-attachable";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });
        await factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiScopes.AddRange(
                new ApiScope { Name = attachedScopeName, Enabled = true },
                new ApiScope { Name = attachableScopeName, Enabled = true });
            await configDb.SaveChangesAsync();

            IApiResourceEditorService editorService = sp.GetRequiredService<IApiResourceEditorService>();
            await editorService.AttachScopeAsync(ScopeName.Create(name), ScopeName.Create(attachedScopeName));
        });

        HttpResponseMessage response = await client.GetAsync($"/Admin/Apis/Editor?name={name}&tab=scopes");
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal(new[] { "Basics", "Secrets", "Scopes", "Claims" },
            document.QuerySelectorAll("#api-resource-editor-tabs > ck-tab")
                .Select(tab => tab.GetAttribute("label")));

        var formActions = document.QuerySelectorAll("form")
            .Select(form => form.GetAttribute("action") ?? string.Empty)
            .ToList();

        foreach (string handler in new[]
                     { "SaveBasics", "AddSecret", "RevokeSecret", "AttachScope", "DetachScope", "Delete" })
            Assert.Contains(formActions, action => action.Contains($"handler={handler}", StringComparison.Ordinal));
    }

    // ---------- Secret generation banner ----------

    [Fact]
    public async Task PostAddSecret_ValidResource_RedirectsToSecretsTabWithOneTimeSecretBanner()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient();
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Apis/Editor?name={name}&tab=secrets");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Apis/Editor?name={name}&handler=AddSecret");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = name,
            ["Secret.Description"] = "CI secret"
        });

        HttpResponseMessage response = await client.SendAsync(request);
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IElement? banner = document.QuerySelector("#generated-secret-banner");
        Assert.NotNull(banner);
        string? secretValue = document.QuerySelector("#generated-secret-value")?.TextContent;
        Assert.False(string.IsNullOrWhiteSpace(secretValue));
    }

    [Fact]
    public async Task PostAddSecret_CraftedPastExpiration_IsRejectedWithoutMutation()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient(false);
        string name = $"{Guid.NewGuid():N}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });
        (string token, string cookie) = await ExtractAntiForgeryTokenAndCookieAsync(
            client,
            $"/Admin/Apis/Editor?name={name}&tab=secrets");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Apis/Editor?name={name}&handler=AddSecret");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = name,
            ["Secret.Description"] = "crafted secret",
            ["Secret.Expiration"] = "2020-01-01T00:00:00"
        });

        HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IDocument document = await GetDocumentAsync(response);
        Assert.Null(document.QuerySelector("#generated-secret-banner"));

        await factory.RunInScopeAsync(async services =>
        {
            ApiResource resource = await services.GetRequiredService<ConfigurationDbContext>().ApiResources
                .AsNoTracking().Include(item => item.Secrets)
                .SingleAsync(item => item.Name == name);
            Assert.Empty(resource.Secrets);
        });
    }

    [Fact]
    public async Task GetAfterAddSecret_SecondPageLoad_DoesNotShowSecretAgain()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient();
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Apis/Editor?name={name}&tab=secrets");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Apis/Editor?name={name}&handler=AddSecret");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = name
        });
        await client.SendAsync(request);

        // TempData is consumed after the redirect-target GET; a fresh GET must not repeat the banner.
        HttpResponseMessage secondResponse = await client.GetAsync($"/Admin/Apis/Editor?name={name}&tab=secrets");
        IDocument secondDocument = await GetDocumentAsync(secondResponse);

        Assert.Null(secondDocument.QuerySelector("#generated-secret-banner"));
    }

    // ---------- Type-to-confirm delete modal (server-side contract) ----------

    [Fact]
    public async Task Get_ExistingResource_RendersDeleteConfirmModalWithDisabledSubmitButton()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient();
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });

        HttpResponseMessage response = await client.GetAsync($"/Admin/Apis/Editor?name={name}");
        IDocument document = await GetDocumentAsync(response);

        IElement? deleteModal = document.QuerySelector("#delete-modal");
        Assert.NotNull(deleteModal);
        IElement? submitButton = deleteModal!.QuerySelector("[data-confirm-submit]");
        Assert.NotNull(submitButton);
        Assert.True(submitButton!.HasAttribute("disabled"));
        IElement? confirmInput = deleteModal.QuerySelector(".confirm-input");
        Assert.Equal("DELETE", confirmInput?.GetAttribute("data-confirm-word"));
        Assert.Equal("confirmation", confirmInput?.GetAttribute("name"));
    }

    [Fact]
    public async Task PostDelete_ExistingResource_RemovesResourceAndRedirectsToIndex()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient(false);
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Apis/Editor?name={name}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Apis/Editor?name={name}&handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = name,
            ["confirmation"] = "DELETE"
        });

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Apis", response.Headers.Location?.OriginalString);

        await factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            Assert.False(await configDb.ApiResources.AnyAsync(r => r.Name == name));
        });
    }

    [Fact]
    public async Task PostDelete_WithoutConfirmation_DoesNotDeleteResource()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient(false);
        string name = $"{Guid.NewGuid():N}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Apis/Editor?name={name}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Apis/Editor?name={name}&handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = name
        });

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Admin/Apis/Editor?name={name}&tab=basics", response.Headers.Location?.OriginalString);
        await factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            Assert.True(await configDb.ApiResources.AnyAsync(r => r.Name == name));
        });
    }

    [Fact]
    public async Task PostDelete_ResourceDoesNotExist_Returns404()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient(false);
        string tag = Guid.NewGuid().ToString("N");
        string existingName = $"{tag}-api";
        string missingName = $"{tag}-missing";
        await SeedApiResourceAsync(factory, new ApiResource { Name = existingName, Enabled = true });

        // The editor GET for a non-existent name 404s too, so the antiforgery token/cookie is
        // pulled from a real resource's editor page instead (tokens aren't tied to route data).
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Apis/Editor?name={existingName}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Apis/Editor?name={missingName}&handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = missingName,
            ["confirmation"] = "DELETE"
        });

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_NonExistentResourceName_Returns404()
    {
        (_, HttpClient client) = CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/Admin/Apis/Editor?name={Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Breadcrumbs (WI-03) ----------

    [Fact]
    public async Task Get_ExistingResource_RendersThreeLevelBreadcrumbChainEndingWithResourceName()
    {
        (AdminWebFactory factory, HttpClient client) = CreateClient();
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });

        HttpResponseMessage response = await client.GetAsync($"/Admin/Apis/Editor?name={name}");
        IDocument document = await GetDocumentAsync(response);

        IHtmlCollection<IElement> crumbLinks = document.QuerySelectorAll("nav.crumbs a");
        Assert.Equal(2, crumbLinks.Length);

        // Both crumbs previously pointed at /Admin/Apis/Index, so "Admin" and "API Resources"
        // led to the same page. Every other view points its "Admin" crumb at the dashboard.
        //
        // The hrefs are route-generated from the page names /Admin/Index and /Admin/Apis/Index,
        // and link generation emits the canonical short form for an Index page — so "/Admin"
        // and "/Admin/Apis", not the literal paths the views name. Both forms route to the
        // same page; this asserts what a browser actually receives.
        Assert.Equal("/Admin", crumbLinks[0].GetAttribute("href"));
        Assert.Equal("Admin", crumbLinks[0].TextContent.Trim());
        Assert.Equal("/Admin/Apis", crumbLinks[1].GetAttribute("href"));
        Assert.Equal("API Resources", crumbLinks[1].TextContent.Trim());

        IElement? current = document.QuerySelector("nav.crumbs .current");
        Assert.NotNull(current);
        Assert.Equal(name, current!.TextContent.Trim());
    }

    [Fact]
    public async Task Get_NewResourceCreateMode_RendersBreadcrumbEndingWithNewApiResource()
    {
        (_, HttpClient client) = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/Admin/Apis/Editor");
        IDocument document = await GetDocumentAsync(response);

        IElement? current = document.QuerySelector("nav.crumbs .current");
        Assert.NotNull(current);
        Assert.Equal("New API Resource", current!.TextContent.Trim());
    }
}