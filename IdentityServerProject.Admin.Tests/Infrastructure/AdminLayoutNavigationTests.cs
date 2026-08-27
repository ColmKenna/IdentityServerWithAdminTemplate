using AngleSharp;
using AngleSharp.Dom;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

/// <summary>
///     Verifies _AdminLayout's sidebar active-link highlighting (WI-02) uses robust route-prefix
///     matching against Razor Pages route data, rather than page-title string matching or a
///     single hardcoded section special case.
/// </summary>
public class AdminLayoutNavigationTests : IClassFixture<AdminWebFactory>
{
    private readonly HttpClient _client;

    public AdminLayoutNavigationTests(AdminWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    private static string? ActiveNavTip(IDocument document)
    {
        var activeItems = document.QuerySelectorAll("a.nav-item.active").ToList();
        Assert.Single(activeItems);
        return activeItems[0].GetAttribute("data-tip");
    }

    [Fact]
    public async Task Get_ClientsListPage_HighlightsClientsNavItem()
    {
        HttpResponseMessage response = await _client.GetAsync("/Admin/Clients");
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal("Clients", ActiveNavTip(document));
    }

    [Fact]
    public async Task Get_ClientsSubPage_StillHighlightsClientsNavItem()
    {
        // Exercises prefix matching beyond the exact Index page (the card's own example is
        // /Admin/Clients/Secrets, which needs a seeded client id; Create needs none).
        HttpResponseMessage response = await _client.GetAsync("/Admin/Clients/Create");
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal("Clients", ActiveNavTip(document));
    }

    [Fact]
    public async Task Get_UsersEditorSubPage_HighlightsUsersNavItem()
    {
        HttpResponseMessage response = await _client.GetAsync("/Admin/Users/Create");
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal("Users", ActiveNavTip(document));
    }

    [Fact]
    public async Task Get_ApiScopesPage_HighlightsApiScopesNotApiResources()
    {
        // Regression guard: "/Admin/ApiScopes" starts with "/Admin/Apis" as a raw string
        // (case-insensitive "ApiS..." vs "Apis"), which a naive StartsWith prefix check
        // would incorrectly treat as a match for the "API Resources" section too.
        HttpResponseMessage response = await _client.GetAsync("/Admin/ApiScopes");
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal("API Scopes", ActiveNavTip(document));
    }

    [Fact]
    public async Task Get_DiagnosticsPage_HighlightsDiagnosticsNavItem()
    {
        HttpResponseMessage response = await _client.GetAsync("/Admin/Diagnostics");
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal("Diagnostics", ActiveNavTip(document));
    }

    [Fact]
    public async Task Get_RolesPage_HighlightsRolesNavItem()
    {
        HttpResponseMessage response = await _client.GetAsync("/Admin/Roles");
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal("Roles", ActiveNavTip(document));
    }

    [Fact]
    public async Task Get_KeysPage_HighlightsKeysNavItem()
    {
        HttpResponseMessage response = await _client.GetAsync("/Admin/Keys");
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal("Signing Keys", ActiveNavTip(document));
    }
}