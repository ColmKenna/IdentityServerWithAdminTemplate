using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Clients;

/// <summary>
///     Locks in the removal of the sub-navigation tab strip that the five Client editor pages
///     once shared (WI-07): no editor page renders the strip, and each one carries a standalone
///     back link to the client's Details page instead.
/// </summary>
public class ClientEditorTabsIntegrationTests : IDisposable
{
    private const string ClientId = "coop.market.razor";

    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

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

        WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                Mock<IClientOverviewService> mock = MockService();
                services.AddSingleton(mock.Object);
                services.AddSingleton(mock.As<IClientAuthenticationService>().Object);
                services.AddSingleton(mock.As<IClientPermissionsService>().Object);
                services.AddSingleton(mock.As<IClientSecretsService>().Object);
                services.AddSingleton(mock.As<IClientTokenSettingsService>().Object);
            });
        });
        _disposables.Add(factory);

        return factory.CreateClient();
    }

    /// <summary>
    ///     One mock instance surfaced through all five editor interfaces, so a single client can
    ///     walk all five URLs. Moq's As&lt;T&gt;() keeps it one object, not five.
    /// </summary>
    private static Mock<IClientOverviewService> MockService()
    {
        var mock = new Mock<IClientOverviewService>();

        mock.Setup(s =>
                s.GetClientDetailsAsync(Services.Clients.ClientId.Create(ClientId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientDetailsModel
            {
                ClientId = Services.Clients.ClientId.Create(ClientId),
                ClientName = "Co-op Market Razor Client",
                Description = "Storefront web client",
                ClientType = "SPA with BFF",
                AllowedGrantTypes = "authorization_code",
                Enabled = true
            });

        mock.As<IClientAuthenticationService>().Setup(s =>
                s.GetClientAuthenticationAsync(Services.Clients.ClientId.Create(ClientId),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientAuthenticationModel
            {
                ClientId = Services.Clients.ClientId.Create(ClientId),
                ClientName = "Co-op Market Razor Client",
                RequirePkce = true,
                RequireClientSecret = true,
                GrantTypes = new List<string> { "authorization_code" }
            });

        mock.As<IClientPermissionsService>().Setup(s =>
                s.GetClientPermissionsAsync(Services.Clients.ClientId.Create(ClientId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientPermissionsModel
            {
                ClientId = Services.Clients.ClientId.Create(ClientId),
                ClientName = "Co-op Market Razor Client",
                IsInteractive = true,
                AllowedScopes = new List<string> { "openid" },
                AvailableIdentityScopes = new List<string> { "openid", "profile" },
                AvailableApiScopes = new List<string> { "coop.market.api" }
            });

        mock.As<IClientSecretsService>().Setup(s =>
                s.GetClientSecretsAsync(Services.Clients.ClientId.Create(ClientId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientSecretsModel
            {
                ClientId = Services.Clients.ClientId.Create(ClientId),
                ClientName = "Co-op Market Razor Client",
                RequireClientSecret = true,
                Secrets = new List<ClientSecretSummary>()
            });

        mock.As<IClientTokenSettingsService>().Setup(s =>
                s.GetClientTokenSettingsAsync(Services.Clients.ClientId.Create(ClientId),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientTokenSettingsModel
            {
                ClientId = Services.Clients.ClientId.Create(ClientId),
                ClientName = "Co-op Market Razor Client",
                AccessTokenLifetime = TokenLifetime.FromSeconds(300),
                IdentityTokenLifetime = TokenLifetime.FromSeconds(300
                )
            });

        return mock;
    }

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    [Theory]
    [MemberData(nameof(EditorPages))]
    public async Task EditorPage_DoesNotRenderTabStrip(string url, string _)
    {
        HttpClient httpClient = CreateClient();

        HttpResponseMessage response = await httpClient.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);
        IElement? nav = document.QuerySelector("ck-tabs[data-client-editor-nav]");
        Assert.Null(nav);
    }

    [Theory]
    [MemberData(nameof(EditorPages))]
    public async Task EditorPage_RendersStandaloneBackLink_ToClientDetails(string url, string _)
    {
        HttpClient httpClient = CreateClient();

        HttpResponseMessage response = await httpClient.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);
        var backLinks = document
            .QuerySelectorAll(".page-header a")
            .Where(a => a.GetAttribute("href")?.Contains($"/Admin/Clients/Details/{ClientId}", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.NotEmpty(backLinks);
    }
}