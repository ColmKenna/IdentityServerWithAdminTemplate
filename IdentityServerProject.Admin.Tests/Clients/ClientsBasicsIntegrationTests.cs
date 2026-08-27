using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientsBasicsIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    private HttpClient CreateClient(IClientDetailsService clientDetailsService, bool allowAutoRedirect = true)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(clientDetailsService);
            });
        });
        _disposables.Add(factory);

        return factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
    }

    private static ClientDetailsModel SampleClientDetails(string id = "coop.market.razor") => new()
    {
        ClientId = ClientId.Create(id),
        ClientName = "Co-op Market Razor Client",
        Description = "Original Description",
        ClientType = "SPA with BFF",
        Enabled = true,
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
        AllowedScopes = new() { "openid", "profile", "coop.market.api" }
    };

    private static IClientDetailsService MockService(ClientDetailsModel? details = null, bool updateSuccess = true)
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientDetailsAsync(It.IsAny<ClientId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientId id, CancellationToken _) => id.Value == "non-existent" ? null : (details ?? SampleClientDetails(id.Value)));
        mock.Setup(s => s.UpdateClientBasicsAsync(It.IsAny<ClientId>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateSuccess
                ? AdminMutationResult.Success()
                : AdminMutationResult.ValidationFailure("Input.ClientName", "Update failed."));
        return mock.Object;
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

    [Fact]
    public async Task Get_ExistingClient_Returns200OK_WithFormAndDisabledClientId()
    {
        var details = SampleClientDetails("test-client");
        var service = MockService(details);
        var httpClient = CreateClient(service);

        var response = await httpClient.GetAsync("/Admin/Clients/Basics/test-client");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);
        
        var clientNameInput = document.QuerySelector("input[name='Input.ClientName']") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(clientNameInput);
        Assert.Equal("Co-op Market Razor Client", clientNameInput!.Value);

        var descriptionArea = document.QuerySelector("textarea[name='Input.Description']") as AngleSharp.Html.Dom.IHtmlTextAreaElement;
        Assert.NotNull(descriptionArea);
        Assert.Equal("Original Description", descriptionArea!.Value);

        var clientIdInput = document.QuerySelector("input[disabled]") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(clientIdInput);
        Assert.Equal("test-client", clientIdInput!.Value);
    }

    [Fact]
    public async Task Get_NonExistentClient_ReturnsNotFound()
    {
        var service = MockService();
        var httpClient = CreateClient(service);

        var response = await httpClient.GetAsync("/Admin/Clients/Basics/non-existent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ValidUpdates_RedirectsToDetails()
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientDetailsAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleClientDetails("test-client"));
        mock.Setup(s => s.UpdateClientBasicsAsync(ClientId.Create("test-client"), "Updated Name", "Updated Desc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        var httpClient = CreateClient(mock.Object, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Basics/test-client");

        var formValues = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Input.ClientName", "Updated Name" },
            { "Input.Description", "Updated Desc" }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Basics/test-client")
        {
            Content = new FormUrlEncodedContent(formValues)
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Clients/Details/test-client", response.Headers.Location?.OriginalString);

        mock.Verify(s => s.UpdateClientBasicsAsync(ClientId.Create("test-client"), "Updated Name", "Updated Desc", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_InvalidData_ReturnsFormWithValidationError()
    {
        var details = SampleClientDetails("test-client");
        var service = MockService(details);
        var httpClient = CreateClient(service, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Basics/test-client");

        var formValues = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Input.ClientName", "" },
            { "Input.Description", "Updated Desc" }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Basics/test-client")
        {
            Content = new FormUrlEncodedContent(formValues)
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);
        var summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}


