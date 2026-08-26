using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using IdentityServerProject.Services.Clients;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Clients;

/// <summary>
/// Covers the sub-navigation tab strip shared by the five Client editor pages (WI-07):
/// every editor page renders the same five tabs, the current page's tab is the active one,
/// each tab carries the link to its sibling page, and the old per-page back links are gone.
/// </summary>
public class ClientEditorTabsIntegrationTests : IDisposable
{
    private const string ClientId = "coop.market.razor";

    private readonly List<IDisposable> _disposables = new();

    /// <summary>The tab strip as the partial declares it: heading label and target URL, in order.</summary>
    private static readonly (string Label, string Url)[] ExpectedTabs =
    {
        ("Basics", $"/Admin/Clients/Basics/{ClientId}"),
        ("Authentication", $"/Admin/Clients/Authentication/{ClientId}"),
        ("Permissions", $"/Admin/Clients/Permissions/{ClientId}"),
        ("Secrets", $"/Admin/Clients/Secrets/{ClientId}"),
        ("Token Settings", $"/Admin/Clients/TokenSettings/{ClientId}")
    };

    public static TheoryData<string, string> EditorPages() => new()
    {
        { $"/Admin/Clients/Basics/{ClientId}", "Basics" },
        { $"/Admin/Clients/Authentication/{ClientId}", "Authentication" },
        { $"/Admin/Clients/Permissions/{ClientId}", "Permissions" },
        { $"/Admin/Clients/Secrets/{ClientId}", "Secrets" },
        { $"/Admin/Clients/TokenSettings/{ClientId}", "Token Settings" }
    };

    private HttpClient CreateClient()
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(MockService());
            });
        });
        _disposables.Add(factory);

        return factory.CreateClient();
    }

    /// <summary>
    /// One mock serving every editor page, so a single client can walk all five URLs.
    /// </summary>
    private static IClientDetailsService MockService()
    {
        var mock = new Mock<IClientDetailsService>();

        mock.Setup(s => s.GetClientDetailsAsync(IdentityServerProject.Services.Clients.ClientId.Create(ClientId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientDetailsModel
            {
                ClientId = IdentityServerProject.Services.Clients.ClientId.Create(ClientId),
                ClientName = "Co-op Market Razor Client",
                Description = "Storefront web client",
                ClientType = "SPA with BFF",
                AllowedGrantTypes = "authorization_code",
                Enabled = true
            });

        mock.Setup(s => s.GetClientAuthenticationAsync(IdentityServerProject.Services.Clients.ClientId.Create(ClientId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientAuthenticationModel
            {
                ClientId = IdentityServerProject.Services.Clients.ClientId.Create(ClientId),
                ClientName = "Co-op Market Razor Client",
                RequirePkce = true,
                RequireClientSecret = true,
                GrantTypes = new() { "authorization_code" }
            });

        mock.Setup(s => s.GetClientPermissionsAsync(ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientPermissionsModel
            {
                ClientId = IdentityServerProject.Services.Clients.ClientId.Create(ClientId),
                ClientName = "Co-op Market Razor Client",
                IsInteractive = true,
                AllowedScopes = new() { "openid" },
                AvailableIdentityScopes = new() { "openid", "profile" },
                AvailableApiScopes = new() { "coop.market.api" }
            });

        mock.Setup(s => s.GetClientSecretsAsync(IdentityServerProject.Services.Clients.ClientId.Create(ClientId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientSecretsModel
            {
                ClientId = IdentityServerProject.Services.Clients.ClientId.Create(ClientId),
                ClientName = "Co-op Market Razor Client",
                RequireClientSecret = true,
                Secrets = new()
            });

        mock.Setup(s => s.GetClientTokenSettingsAsync(IdentityServerProject.Services.Clients.ClientId.Create(ClientId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientTokenSettingsModel
            {
                ClientId = IdentityServerProject.Services.Clients.ClientId.Create(ClientId),
                ClientName = "Co-op Market Razor Client",
                AccessTokenLifetime = TokenLifetime.FromSeconds(300),
                IdentityTokenLifetime = TokenLifetime.FromSeconds(300
            )});

        return mock.Object;
    }

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    private static IElement GetNavStrip(IDocument document)
    {
        var nav = document.QuerySelector("ck-tabs[data-client-editor-nav]");
        Assert.NotNull(nav);
        return nav!;
    }

    [Theory]
    [MemberData(nameof(EditorPages))]
    public async Task EditorPage_RendersAllFiveTabs_InOrder(string url, string _)
    {
        var httpClient = CreateClient();

        var response = await httpClient.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);
        var tabs = GetNavStrip(document).QuerySelectorAll("ck-tab");

        Assert.Equal(
            ExpectedTabs.Select(t => t.Label),
            tabs.Select(t => t.GetAttribute("label")));
    }

    [Theory]
    [MemberData(nameof(EditorPages))]
    public async Task EditorPage_MarksOnlyItsOwnTabActive(string url, string expectedActiveLabel)
    {
        var httpClient = CreateClient();

        var response = await httpClient.GetAsync(url);
        var document = await GetDocumentAsync(response);
        var tabs = GetNavStrip(document).QuerySelectorAll("ck-tab");

        var activeTabs = tabs.Where(t => t.HasAttribute("active")).ToList();
        var activeTab = Assert.Single(activeTabs);
        Assert.Equal(expectedActiveLabel, activeTab.GetAttribute("label"));

        // The script reads data-current to avoid re-navigating to the page already open,
        // so it has to agree with the component's own active state.
        var currentTabs = tabs.Where(t => t.GetAttribute("data-current") == "true").ToList();
        Assert.Same(activeTab, Assert.Single(currentTabs));

        var activeLink = activeTab.QuerySelector("a");
        Assert.NotNull(activeLink);
        Assert.Equal("page", activeLink!.GetAttribute("aria-current"));
    }

    [Theory]
    [MemberData(nameof(EditorPages))]
    public async Task EditorPage_TabsLinkToSiblingEditorPagesForTheSameClient(string url, string _)
    {
        var httpClient = CreateClient();

        var response = await httpClient.GetAsync(url);
        var document = await GetDocumentAsync(response);
        var tabs = GetNavStrip(document).QuerySelectorAll("ck-tab");

        var links = tabs
            .Select(t => t.QuerySelector("a")?.GetAttribute("href"))
            .ToList();

        Assert.Equal(ExpectedTabs.Select(t => t.Url), links);
    }

    [Theory]
    [MemberData(nameof(EditorPages))]
    public async Task EditorPage_NoLongerRendersStandaloneBackLink(string url, string _)
    {
        var httpClient = CreateClient();

        var response = await httpClient.GetAsync(url);
        var document = await GetDocumentAsync(response);

        var backLinks = document
            .QuerySelectorAll(".page-header a")
            .Where(a => a.TextContent.Contains("Back to Client Hub", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(backLinks);
    }

    [Theory]
    [MemberData(nameof(EditorPages))]
    public async Task EditorPage_LoadsTabComponentAndNavigationScript(string url, string _)
    {
        var httpClient = CreateClient();

        var response = await httpClient.GetAsync(url);
        var document = await GetDocumentAsync(response);

        var sources = document
            .QuerySelectorAll("script[src]")
            .Select(s => s.GetAttribute("src") ?? string.Empty)
            .ToList();

        Assert.Contains(sources, src => src.Contains("/lib/ck-tabs-webcomponent/index.esm.js", StringComparison.Ordinal));
        Assert.Contains(sources, src => src.Contains("/js/client-editor-tabs.js", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}
