using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientsAuthenticationIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private HttpClient CreateClient(IClientAuthenticationService clientDetailsService, bool allowAutoRedirect = true)
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

    private static ClientAuthenticationModel SampleAuthentication(string id = "test-client") => new()
    {
        ClientId = ClientId.Create(id),
        ClientName = "Co-op Market Razor Client",
        RequirePkce = true,
        RequireClientSecret = true,
        GrantTypes = new List<string> { "authorization_code" },
        RedirectUris = new List<string> { "https://localhost:5001/signin-oidc" },
        AllowedCorsOrigins = new List<string> { "https://localhost:5001" },
        HasDrifted = false,
        DriftDetails = null
    };

    private static IClientAuthenticationService MockService(ClientAuthenticationModel? details = null,
        bool updateSuccess = true)
    {
        var mock = new Mock<IClientAuthenticationService>();
        mock.Setup(s => s.GetClientAuthenticationAsync(It.IsAny<ClientId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientId id, CancellationToken _) =>
                id.Value == "non-existent" ? null : details ?? SampleAuthentication(id.Value));
        mock.Setup(s => s.UpdateClientAuthenticationAsync(It.IsAny<ClientId>(),
                It.IsAny<ClientAuthenticationInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientId id, ClientAuthenticationInputModel _, CancellationToken _) =>
                id.Value == "non-existent"
                    ? AdminMutationResult.NotFoundResult()
                    : updateSuccess
                        ? AdminMutationResult.Success()
                        : AdminMutationResult.ValidationFailure("Input", "Error"));
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
    public async Task Get_ExistingClient_Returns200OK_WithCurrentConfiguration()
    {
        HttpClient httpClient = CreateClient(MockService());

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Clients/Authentication/test-client");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);

        var pkceCheckbox = document.QuerySelector("input[name='Input.RequirePkce']") as IHtmlInputElement;
        Assert.NotNull(pkceCheckbox);
        Assert.True(pkceCheckbox!.IsChecked);

        var authCodeCheckbox =
            document.QuerySelector("input[name='Input.GrantTypes'][value='authorization_code']") as IHtmlInputElement;
        Assert.NotNull(authCodeCheckbox);
        Assert.True(authCodeCheckbox!.IsChecked);

        var redirectUriInput = document.QuerySelector("input[name='Input.RedirectUris']") as IHtmlInputElement;
        Assert.NotNull(redirectUriInput);
        Assert.Equal("https://localhost:5001/signin-oidc", redirectUriInput!.Value);

        var corsOriginInput = document.QuerySelector("input[name='Input.CorsOrigins']") as IHtmlInputElement;
        Assert.NotNull(corsOriginInput);
        Assert.Equal("https://localhost:5001", corsOriginInput!.Value);
    }

    [Fact]
    public async Task Get_NonExistentClient_ReturnsNotFound()
    {
        HttpClient httpClient = CreateClient(MockService());

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Clients/Authentication/non-existent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_DriftedClient_RendersDriftBadge()
    {
        ClientAuthenticationModel details = SampleAuthentication();
        details.HasDrifted = true;
        details.DriftDetails = "Client secret is not required, preset 'web' expects required";
        HttpClient httpClient = CreateClient(MockService(details));

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Clients/Authentication/test-client");
        IDocument document = await GetDocumentAsync(response);

        IElement? badge = document.QuerySelector(".drift-badge");
        Assert.NotNull(badge);
        Assert.Contains("Drifted from preset", badge!.TextContent);
    }

    [Fact]
    public async Task Get_NonDriftedClient_DoesNotRenderDriftBadge()
    {
        HttpClient httpClient = CreateClient(MockService(SampleAuthentication()));

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Clients/Authentication/test-client");
        IDocument document = await GetDocumentAsync(response);

        Assert.Null(document.QuerySelector(".drift-badge"));
    }

    [Fact]
    public async Task Post_AddingRedirectUri_RedirectsToDetailsAndPersists()
    {
        var mock = new Mock<IClientAuthenticationService>();
        mock.Setup(s => s.GetClientAuthenticationAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAuthentication());
        mock.Setup(s => s.UpdateClientAuthenticationAsync(ClientId.Create("test-client"),
                It.IsAny<ClientAuthenticationInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Authentication/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("true"), "Input.RequirePkce");
        content.Add(new StringContent("true"), "Input.RequireClientSecret");
        content.Add(new StringContent("authorization_code"), "Input.GrantTypes");
        content.Add(new StringContent("https://localhost:5001/signin-oidc"), "Input.RedirectUris");
        content.Add(new StringContent("https://newapp.example.com/callback"), "Input.RedirectUris");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Authentication/test-client")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Clients/Details/test-client", response.Headers.Location?.OriginalString);

        mock.Verify(s => s.UpdateClientAuthenticationAsync(
            ClientId.Create("test-client"),
            It.Is<ClientAuthenticationInputModel>(m =>
                m.RedirectUris.Contains("https://newapp.example.com/callback") &&
                m.RedirectUris.Contains("https://localhost:5001/signin-oidc")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_RemovingRedirectUri_PersistsRemoval()
    {
        var mock = new Mock<IClientAuthenticationService>();
        mock.Setup(s => s.GetClientAuthenticationAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAuthentication());
        mock.Setup(s => s.UpdateClientAuthenticationAsync(ClientId.Create("test-client"),
                It.IsAny<ClientAuthenticationInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Authentication/test-client");

        // Simulate the redirect URI row being removed client-side: submit without it.
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("true"), "Input.RequirePkce");
        content.Add(new StringContent("true"), "Input.RequireClientSecret");
        content.Add(new StringContent("authorization_code"), "Input.GrantTypes");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Authentication/test-client")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        mock.Verify(s => s.UpdateClientAuthenticationAsync(
            ClientId.Create("test-client"),
            It.Is<ClientAuthenticationInputModel>(m => m.RedirectUris.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_ChangingGrantTypes_PersistsNewSelection()
    {
        var mock = new Mock<IClientAuthenticationService>();
        mock.Setup(s => s.GetClientAuthenticationAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAuthentication());
        mock.Setup(s => s.UpdateClientAuthenticationAsync(ClientId.Create("test-client"),
                It.IsAny<ClientAuthenticationInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Authentication/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("false"), "Input.RequirePkce");
        content.Add(new StringContent("true"), "Input.RequireClientSecret");
        content.Add(new StringContent("client_credentials"), "Input.GrantTypes");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Authentication/test-client")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        mock.Verify(s => s.UpdateClientAuthenticationAsync(
            ClientId.Create("test-client"),
            It.Is<ClientAuthenticationInputModel>(m => m.GrantTypes.Single() == "client_credentials" && !m.RequirePkce),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_NoGrantTypesSelected_ReturnsValidationError()
    {
        HttpClient httpClient = CreateClient(MockService(), false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Authentication/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("true"), "Input.RequirePkce");
        content.Add(new StringContent("true"), "Input.RequireClientSecret");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Authentication/test-client")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IDocument document = await GetDocumentAsync(response);
        IElement? summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);
        Assert.Contains("grant type", summary!.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_MalformedRedirectUri_ReturnsValidationErrorWithUrisTabActive()
    {
        HttpClient httpClient = CreateClient(MockService(), false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Authentication/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("authorization_code"), "Input.GrantTypes");
        content.Add(new StringContent("not-a-url"), "Input.RedirectUris");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Authentication/test-client")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IDocument document = await GetDocumentAsync(response);
        IElement? activeTab = document.QuerySelector("#client-authentication-tabs > ck-tab[active]");
        Assert.NotNull(activeTab);
        Assert.Equal("Redirect & CORS URIs", activeTab!.GetAttribute("label"));
        Assert.Contains("absolute HTTP or HTTPS URL", document.QuerySelector(".validation-summary")!.TextContent);
    }

    [Fact]
    public async Task Post_OverlongAuthenticationValues_ReturnsFieldErrorsAndDoesNotCallMutationService()
    {
        var mock = new Mock<IClientAuthenticationService>();
        mock.Setup(s => s.GetClientAuthenticationAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAuthentication());

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Authentication/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent(new string('g', ValidationConstants.MaxGrantTypeLength + 1)), "Input.GrantTypes");
        content.Add(
            new StringContent("https://example.com/" + new string('r', ValidationConstants.MaxClientRedirectUriLength)),
            "Input.RedirectUris");
        content.Add(
            new StringContent("https://example.com/" +
                              new string('p', ValidationConstants.MaxClientPostLogoutRedirectUriLength)),
            "Input.PostLogoutRedirectUris");
        content.Add(
            new StringContent("https://example.com/" + new string('c', ValidationConstants.MaxClientCorsOriginLength)),
            "Input.CorsOrigins");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Authentication/test-client")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IDocument document = await GetDocumentAsync(response);
        IElement? summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);
        Assert.Contains("Grant types cannot exceed", summary!.TextContent);
        Assert.Contains("absolute HTTP or HTTPS URL", summary.TextContent);
        IElement? activeTab = document.QuerySelector("#client-authentication-tabs > ck-tab[active]");
        Assert.Equal("Redirect & CORS URIs", activeTab?.GetAttribute("label"));
        mock.Verify(
            service => service.UpdateClientAuthenticationAsync(
                It.IsAny<ClientId>(), It.IsAny<ClientAuthenticationInputModel>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Post_NonExistentClient_ReturnsNotFound()
    {
        HttpClient httpClient = CreateClient(MockService(), false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Authentication/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("authorization_code"), "Input.GrantTypes");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Authentication/non-existent")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}