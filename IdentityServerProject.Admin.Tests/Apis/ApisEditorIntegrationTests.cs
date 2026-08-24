using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Apis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Apis;

/// <summary>
/// Full HTTP pipeline tests for /Admin/Apis/Editor (WI-14), covering the three Scenario
/// Review points agreed at plan approval: tab switching via query string, the one-time
/// secret generation banner, and the server-side contract behind the type-to-confirm
/// delete modal. Uses a real (SQLite in-memory) database via <see cref="AdminWebFactory"/>
/// so the redirect/TempData/antiforgery pipeline is exercised end to end.
/// </summary>
public class ApisEditorIntegrationTests : IDisposable
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

    private static async Task SeedApiResourceAsync(AdminWebFactory factory, ApiResource resource)
    {
        await factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiResources.Add(resource);
            await configDb.SaveChangesAsync();
        });
    }

    // ---------- Tab switching via query string ----------

    [Fact]
    public async Task Get_NoTabQueryString_DefaultsToBasicsTabActive()
    {
        var (factory, client) = CreateClient();
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });

        var response = await client.GetAsync($"/Admin/Apis/Editor?name={name}");
        var document = await GetDocumentAsync(response);

        var activeTab = document.QuerySelector("#api-resource-editor-tabs > ck-tab[active]");
        Assert.NotNull(activeTab);
        Assert.Equal("Basics", activeTab!.GetAttribute("label"));
    }

    [Fact]
    public async Task Get_TabQueryStringSecrets_MarksSecretsTabActiveAndRendersSecretsPanel()
    {
        var (factory, client) = CreateClient();
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });

        var response = await client.GetAsync($"/Admin/Apis/Editor?name={name}&tab=secrets");
        var document = await GetDocumentAsync(response);

        var activeTab = document.QuerySelector("#api-resource-editor-tabs > ck-tab[active]");
        Assert.NotNull(activeTab);
        Assert.Equal("Secrets", activeTab!.GetAttribute("label"));
        Assert.NotNull(document.QuerySelector(".editor-secrets-table"));
    }

    [Fact]
    public async Task Get_NewResourceCreateMode_RendersOnlyTheUsableBasicsTab()
    {
        var (_, client) = CreateClient();

        var response = await client.GetAsync("/Admin/Apis/Editor");
        var document = await GetDocumentAsync(response);

        var tabs = document.QuerySelectorAll("#api-resource-editor-tabs > ck-tab");
        Assert.Single(tabs);
        Assert.Equal("Basics", tabs[0].GetAttribute("label"));
    }

    [Fact]
    public async Task Get_ExistingResource_RendersCkTabsAndAllNamedHandlerForms()
    {
        var (factory, client) = CreateClient();
        var name = $"{Guid.NewGuid():N}-api";
        var attachedScopeName = $"{Guid.NewGuid():N}-attached";
        var attachableScopeName = $"{Guid.NewGuid():N}-attachable";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });
        await factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiScopes.AddRange(
                new ApiScope { Name = attachedScopeName, Enabled = true },
                new ApiScope { Name = attachableScopeName, Enabled = true });
            await configDb.SaveChangesAsync();

            var editorService = sp.GetRequiredService<IApiResourceEditorService>();
            await editorService.AttachScopeAsync(name, attachedScopeName);
        });

        var response = await client.GetAsync($"/Admin/Apis/Editor?name={name}&tab=scopes");
        var document = await GetDocumentAsync(response);

        Assert.Equal(new[] { "Basics", "Secrets", "Scopes", "Claims" },
            document.QuerySelectorAll("#api-resource-editor-tabs > ck-tab")
                .Select(tab => tab.GetAttribute("label")));

        var formActions = document.QuerySelectorAll("form")
            .Select(form => form.GetAttribute("action") ?? string.Empty)
            .ToList();

        foreach (var handler in new[] { "SaveBasics", "AddSecret", "RevokeSecret", "AttachScope", "DetachScope", "Delete" })
        {
            Assert.Contains(formActions, action => action.Contains($"handler={handler}", StringComparison.Ordinal));
        }
    }

    // ---------- Secret generation banner ----------

    [Fact]
    public async Task PostAddSecret_ValidResource_RedirectsToSecretsTabWithOneTimeSecretBanner()
    {
        var (factory, client) = CreateClient(allowAutoRedirect: true);
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Apis/Editor?name={name}&tab=secrets");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Apis/Editor?name={name}&handler=AddSecret");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = name,
            ["Secret.Description"] = "CI secret",
        });

        var response = await client.SendAsync(request);
        var document = await GetDocumentAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var banner = document.QuerySelector("#generated-secret-banner");
        Assert.NotNull(banner);
        var secretValue = document.QuerySelector("#generated-secret-value")?.TextContent;
        Assert.False(string.IsNullOrWhiteSpace(secretValue));
    }

    [Fact]
    public async Task PostAddSecret_CraftedPastExpiration_IsRejectedWithoutMutation()
    {
        var (factory, client) = CreateClient(allowAutoRedirect: false);
        var name = $"{Guid.NewGuid():N}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(
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

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await GetDocumentAsync(response);
        Assert.Null(document.QuerySelector("#generated-secret-banner"));

        await factory.RunInScopeAsync(async services =>
        {
            var resource = await services.GetRequiredService<ConfigurationDbContext>().ApiResources
                .AsNoTracking().Include(item => item.Secrets)
                .SingleAsync(item => item.Name == name);
            Assert.Empty(resource.Secrets);
        });
    }

    [Fact]
    public async Task GetAfterAddSecret_SecondPageLoad_DoesNotShowSecretAgain()
    {
        var (factory, client) = CreateClient(allowAutoRedirect: true);
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Apis/Editor?name={name}&tab=secrets");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Apis/Editor?name={name}&handler=AddSecret");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = name,
        });
        await client.SendAsync(request);

        // TempData is consumed after the redirect-target GET; a fresh GET must not repeat the banner.
        var secondResponse = await client.GetAsync($"/Admin/Apis/Editor?name={name}&tab=secrets");
        var secondDocument = await GetDocumentAsync(secondResponse);

        Assert.Null(secondDocument.QuerySelector("#generated-secret-banner"));
    }

    // ---------- Type-to-confirm delete modal (server-side contract) ----------

    [Fact]
    public async Task Get_ExistingResource_RendersDeleteConfirmModalWithDisabledSubmitButton()
    {
        var (factory, client) = CreateClient();
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });

        var response = await client.GetAsync($"/Admin/Apis/Editor?name={name}");
        var document = await GetDocumentAsync(response);

        var deleteModal = document.QuerySelector("#delete-modal");
        Assert.NotNull(deleteModal);
        var submitButton = deleteModal!.QuerySelector("[data-confirm-submit]");
        Assert.NotNull(submitButton);
        Assert.True(submitButton!.HasAttribute("disabled"));
        var confirmInput = deleteModal.QuerySelector(".confirm-input");
        Assert.Equal("DELETE", confirmInput?.GetAttribute("data-confirm-word"));
        Assert.Equal("confirmation", confirmInput?.GetAttribute("name"));
    }

    [Fact]
    public async Task PostDelete_ExistingResource_RemovesResourceAndRedirectsToIndex()
    {
        var (factory, client) = CreateClient(allowAutoRedirect: false);
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });

        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Apis/Editor?name={name}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Apis/Editor?name={name}&handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = name,
            ["confirmation"] = "DELETE",
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Apis", response.Headers.Location?.OriginalString);

        await factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            Assert.False(await configDb.ApiResources.AnyAsync(r => r.Name == name));
        });
    }

    [Fact]
    public async Task PostDelete_WithoutConfirmation_DoesNotDeleteResource()
    {
        var (factory, client) = CreateClient(allowAutoRedirect: false);
        var name = $"{Guid.NewGuid():N}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Apis/Editor?name={name}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Apis/Editor?name={name}&handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = name,
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Admin/Apis/Editor?name={name}&tab=basics", response.Headers.Location?.OriginalString);
        await factory.RunInScopeAsync(async sp =>
        {
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            Assert.True(await configDb.ApiResources.AnyAsync(r => r.Name == name));
        });
    }

    [Fact]
    public async Task PostDelete_ResourceDoesNotExist_Returns404()
    {
        var (factory, client) = CreateClient(allowAutoRedirect: false);
        var tag = Guid.NewGuid().ToString("N");
        var existingName = $"{tag}-api";
        var missingName = $"{tag}-missing";
        await SeedApiResourceAsync(factory, new ApiResource { Name = existingName, Enabled = true });

        // The editor GET for a non-existent name 404s too, so the antiforgery token/cookie is
        // pulled from a real resource's editor page instead (tokens aren't tied to route data).
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, $"/Admin/Apis/Editor?name={existingName}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Apis/Editor?name={missingName}&handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = missingName,
            ["confirmation"] = "DELETE",
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_NonExistentResourceName_Returns404()
    {
        var (_, client) = CreateClient();

        var response = await client.GetAsync($"/Admin/Apis/Editor?name={Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Breadcrumbs (WI-03) ----------

    [Fact]
    public async Task Get_ExistingResource_RendersThreeLevelBreadcrumbChainEndingWithResourceName()
    {
        var (factory, client) = CreateClient();
        var tag = Guid.NewGuid().ToString("N");
        var name = $"{tag}-api";
        await SeedApiResourceAsync(factory, new ApiResource { Name = name, Enabled = true });

        var response = await client.GetAsync($"/Admin/Apis/Editor?name={name}");
        var document = await GetDocumentAsync(response);

        var crumbLinks = document.QuerySelectorAll("nav.crumbs a");
        Assert.Equal(2, crumbLinks.Length);
        Assert.Equal("/Admin/Apis/Index", crumbLinks[0].GetAttribute("href"));
        Assert.Equal("/Admin/Apis/Index", crumbLinks[1].GetAttribute("href"));

        var current = document.QuerySelector("nav.crumbs .current");
        Assert.NotNull(current);
        Assert.Equal(name, current!.TextContent.Trim());
    }

    [Fact]
    public async Task Get_NewResourceCreateMode_RendersBreadcrumbEndingWithNewApiResource()
    {
        var (_, client) = CreateClient();

        var response = await client.GetAsync("/Admin/Apis/Editor");
        var document = await GetDocumentAsync(response);

        var current = document.QuerySelector("nav.crumbs .current");
        Assert.NotNull(current);
        Assert.Equal("New API Resource", current!.TextContent.Trim());
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}
