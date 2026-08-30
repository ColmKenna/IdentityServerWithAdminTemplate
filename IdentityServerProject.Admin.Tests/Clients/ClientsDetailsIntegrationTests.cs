using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientsDetailsIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private HttpClient CreateClient(IClientOverviewService clientDetailsService, bool allowAutoRedirect = true)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => { services.AddSingleton(clientDetailsService); });
        });
        _disposables.Add(factory);

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
    }

    private static ClientDetailsModel SampleClientDetails(string id = "coop.market.razor", bool enabled = true) => new()
    {
        ClientId = ClientId.Create(id),
        ClientName = "Co-op Market Razor Client",
        Description = "No description seeded or configured.",
        ClientType = "SPA with BFF",
        Enabled = enabled,
        RequirePkce = true,
        RequireClientSecret = true,
        RequireConsent = false,
        AllowOfflineAccess = true,
        AccessTokenLifetime = TokenLifetime.FromSeconds(300),
        AllowedGrantTypes = "authorization_code",
        RedirectUrisCount = 2,
        CorsOriginsCount = 0,
        SecretsCount = 1,
        AllowedScopesCount = 3,
        AllowedScopes = new List<string> { "openid", "profile", "coop.market.api" },
        CanDelete = false,
        DeleteBlockReason = "Client must be disabled before it can be deleted."
    };

    private static IClientOverviewService MockService(
        ClientDetailsModel? details = null,
        bool toggleSuccess = true,
        ClientDeleteResult? deleteResult = null)
    {
        var mock = new Mock<IClientOverviewService>();
        mock.Setup(s => s.GetClientDetailsAsync(It.IsAny<ClientId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientId id, CancellationToken _) =>
                id.Value == "non-existent" ? null : details ?? SampleClientDetails(id.Value));
        mock.Setup(s => s.ToggleClientStatusAsync(It.IsAny<ClientId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(toggleSuccess);
        mock.Setup(s => s.DeleteClientAsync(It.IsAny<ClientId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientId id, CancellationToken _) => id.Value == "non-existent"
                ? ClientDeleteResult.Failed("Client not found.")
                : deleteResult ?? ClientDeleteResult.Failed("Client must be disabled before it can be deleted."));
        return mock.Object;
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

    [Fact]
    public async Task Get_ExistingClient_Returns200OK()
    {
        HttpClient client = CreateClient(MockService());

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients/Details/coop.market.razor");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_NonExistentClient_Returns404NotFound()
    {
        HttpClient client = CreateClient(MockService());

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients/Details/non-existent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_ExistingClient_RendersSidebarWithClientsActive()
    {
        HttpClient client = CreateClient(MockService());

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients/Details/coop.market.razor");
        IDocument document = await GetDocumentAsync(response);

        Assert.NotNull(document.QuerySelector("aside.sidebar"));
        IElement? clientsNavItem = document.QuerySelector("a[href*='/Admin/Clients']");
        Assert.NotNull(clientsNavItem);
        Assert.Contains("active", clientsNavItem!.ClassList);
    }

    [Fact]
    public async Task Get_ExistingClient_RendersHeroHeaderElements()
    {
        HttpClient client = CreateClient(MockService());

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients/Details/coop.market.razor");
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal("Co-op Market Razor Client", document.QuerySelector("h1.hero-title")?.TextContent.Trim());
        Assert.Equal("coop.market.razor", document.QuerySelector("code.client-id-badge")?.TextContent.Trim());
        Assert.Equal("SPA with BFF", document.QuerySelector("span.app-type-badge")?.TextContent.Trim());

        IElement? statusBadge = document.QuerySelector("span.status-badge");
        Assert.NotNull(statusBadge);
        Assert.Contains("• Active", statusBadge!.TextContent);

        IElement? backLink = document.QuerySelector("a.back-link");
        Assert.NotNull(backLink);
        Assert.Contains("Back to list", backLink!.TextContent);
    }

    [Fact]
    public async Task Get_ExistingClient_RendersAllConfigurationSubCards()
    {
        HttpClient client = CreateClient(MockService());

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients/Details/coop.market.razor");
        IDocument document = await GetDocumentAsync(response);

        Assert.NotNull(document.QuerySelector("#card-basics"));
        Assert.NotNull(document.QuerySelector("#card-auth-redirects"));
        Assert.NotNull(document.QuerySelector("#card-secrets"));
        Assert.NotNull(document.QuerySelector("#card-scopes"));
        Assert.NotNull(document.QuerySelector("#card-token-settings"));

        // Scope chips
        var scopeChips = document.QuerySelectorAll("code.scope-chip").Select(c => c.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "openid", "profile", "coop.market.api" }, scopeChips);
    }

    [Fact]
    public async Task Get_ExistingClient_RendersDeactivateCardWithDisableButtonWhenActive()
    {
        HttpClient client = CreateClient(MockService(SampleClientDetails(enabled: true)));

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients/Details/coop.market.razor");
        IDocument document = await GetDocumentAsync(response);

        IElement? deactivateCard = document.QuerySelector("#card-deactivate");
        Assert.NotNull(deactivateCard);
        Assert.Contains("Deactivate Client Application", deactivateCard!.QuerySelector("h2")?.TextContent);

        IElement? submitButton = deactivateCard.QuerySelector("button[type='submit']");
        Assert.NotNull(submitButton);
        Assert.Equal("Disable client", submitButton!.TextContent.Trim());
    }

    [Fact]
    public async Task Get_DisabledClient_RendersDeactivateCardWithEnableButtonWhenDisabled()
    {
        HttpClient client = CreateClient(MockService(SampleClientDetails(enabled: false)));

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients/Details/coop.market.razor");
        IDocument document = await GetDocumentAsync(response);

        IElement? deactivateCard = document.QuerySelector("#card-deactivate");
        Assert.NotNull(deactivateCard);
        Assert.Contains("Activate Client Application", deactivateCard!.QuerySelector("h2")?.TextContent);

        IElement? submitButton = deactivateCard.QuerySelector("button[type='submit']");
        Assert.NotNull(submitButton);
        Assert.Equal("Enable client", submitButton!.TextContent.Trim());
    }

    [Fact]
    public async Task PostToggleStatus_ValidAntiForgery_RedirectsToDetailsPage()
    {
        HttpClient client = CreateClient(MockService(), false);

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/Clients/Details/coop.market.razor");

        var request = new HttpRequestMessage(HttpMethod.Post,
            "/Admin/Clients/Details/coop.market.razor?handler=ToggleStatus");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["DeleteConfirmation"] = "DELETE"
        });

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Clients/Details/coop.market.razor", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Get_ClientNotEligibleForDeletion_RendersDisabledDeleteButton()
    {
        ClientDetailsModel details = SampleClientDetails(enabled: false);
        details.CanDelete = false;
        details.DeleteBlockReason =
            "Client has been disabled for 10 day(s). It can be deleted in 80 more day(s) (90-day retention rule).";
        HttpClient client = CreateClient(MockService(details));

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients/Details/coop.market.razor");
        IDocument document = await GetDocumentAsync(response);

        IElement? deleteCard = document.QuerySelector("#card-delete");
        Assert.NotNull(deleteCard);

        var deleteButton = deleteCard!.QuerySelector("button[type='submit']") as IHtmlButtonElement;
        Assert.NotNull(deleteButton);
        Assert.True(deleteButton!.IsDisabled);
        Assert.Contains(details.DeleteBlockReason, deleteCard.TextContent);
    }

    [Fact]
    public async Task Get_ClientEligibleForDeletion_RendersEnabledDeleteButton()
    {
        ClientDetailsModel details = SampleClientDetails(enabled: false);
        details.CanDelete = true;
        details.DeleteBlockReason = null;
        HttpClient client = CreateClient(MockService(details));

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients/Details/coop.market.razor");
        IDocument document = await GetDocumentAsync(response);

        IElement? deleteCard = document.QuerySelector("#card-delete");
        Assert.NotNull(deleteCard);

        var deleteButton = deleteCard!.QuerySelector("button[type='submit']") as IHtmlButtonElement;
        Assert.NotNull(deleteButton);
        Assert.False(deleteButton!.IsDisabled);
    }

    [Fact]
    public async Task PostDelete_Eligible_RedirectsToIndex()
    {
        HttpClient client = CreateClient(MockService(deleteResult: ClientDeleteResult.Succeeded()), false);

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/Clients/Details/coop.market.razor");

        var request =
            new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Details/coop.market.razor?handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["DeleteConfirmation"] = "DELETE"
        });

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Clients", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task PostDelete_Blocked_RedirectsBackToDetailsWithReason()
    {
        string blockReason = "Client must be disabled before it can be deleted.";
        HttpClient client = CreateClient(MockService(deleteResult: ClientDeleteResult.Failed(blockReason)), false);

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/Clients/Details/coop.market.razor");

        var request =
            new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Details/coop.market.razor?handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["DeleteConfirmation"] = "DELETE"
        });

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Clients/Details/coop.market.razor", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task PostDelete_NonExistentClient_Returns404NotFound()
    {
        HttpClient client = CreateClient(MockService(), false);

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/Clients/Details/coop.market.razor");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Details/non-existent?handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["DeleteConfirmation"] = "DELETE"
        });

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}