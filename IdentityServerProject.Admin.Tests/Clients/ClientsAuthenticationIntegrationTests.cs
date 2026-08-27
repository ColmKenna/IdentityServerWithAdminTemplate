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

public class ClientsAuthenticationIntegrationTests : IDisposable
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

    private static IClientDetailsService MockService(ClientAuthenticationModel? details = null, bool updateSuccess = true)
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientAuthenticationAsync(It.IsAny<ClientId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientId id, CancellationToken _) => id.Value == "non-existent" ? null : (details ?? SampleAuthentication(id.Value)));
        mock.Setup(s => s.UpdateClientAuthenticationAsync(It.IsAny<ClientId>(), It.IsAny<ClientAuthenticationInputModel>(), It.IsAny<CancellationToken>()))
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
    public async Task Get_ExistingClient_Returns200OK_WithCurrentConfiguration()
    {
        var httpClient = CreateClient(MockService());

        var response = await httpClient.GetAsync("/Admin/Clients/Authentication/test-client");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);

        var pkceCheckbox = document.QuerySelector("input[name='Input.RequirePkce']") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(pkceCheckbox);
        Assert.True(pkceCheckbox!.IsChecked);

        var authCodeCheckbox = document.QuerySelector("input[name='Input.GrantTypes'][value='authorization_code']") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(authCodeCheckbox);
        Assert.True(authCodeCheckbox!.IsChecked);

        var redirectUriInput = document.QuerySelector("input[name='Input.RedirectUris']") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(redirectUriInput);
        Assert.Equal("https://localhost:5001/signin-oidc", redirectUriInput!.Value);

        var corsOriginInput = document.QuerySelector("input[name='Input.CorsOrigins']") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(corsOriginInput);
        Assert.Equal("https://localhost:5001", corsOriginInput!.Value);
    }

    [Fact]
    public async Task Get_NonExistentClient_ReturnsNotFound()
    {
        var httpClient = CreateClient(MockService());

        var response = await httpClient.GetAsync("/Admin/Clients/Authentication/non-existent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_DriftedClient_RendersDriftBadge()
    {
        var details = SampleAuthentication();
        details.HasDrifted = true;
        details.DriftDetails = "Client secret is not required, preset 'web' expects required";
        var httpClient = CreateClient(MockService(details));

        var response = await httpClient.GetAsync("/Admin/Clients/Authentication/test-client");
        var document = await GetDocumentAsync(response);

        var badge = document.QuerySelector(".drift-badge");
        Assert.NotNull(badge);
        Assert.Contains("Drifted from preset", badge!.TextContent);
    }

    [Fact]
    public async Task Get_NonDriftedClient_DoesNotRenderDriftBadge()
    {
        var httpClient = CreateClient(MockService(SampleAuthentication()));

        var response = await httpClient.GetAsync("/Admin/Clients/Authentication/test-client");
        var document = await GetDocumentAsync(response);

        Assert.Null(document.QuerySelector(".drift-badge"));
    }

    [Fact]
    public async Task Post_AddingRedirectUri_RedirectsToDetailsAndPersists()
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientAuthenticationAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAuthentication());
        mock.Setup(s => s.UpdateClientAuthenticationAsync(ClientId.Create("test-client"), It.IsAny<ClientAuthenticationInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        var httpClient = CreateClient(mock.Object, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Authentication/test-client");

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

        var response = await httpClient.SendAsync(request);

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
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientAuthenticationAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAuthentication());
        mock.Setup(s => s.UpdateClientAuthenticationAsync(ClientId.Create("test-client"), It.IsAny<ClientAuthenticationInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        var httpClient = CreateClient(mock.Object, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Authentication/test-client");

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

        var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        mock.Verify(s => s.UpdateClientAuthenticationAsync(
            ClientId.Create("test-client"),
            It.Is<ClientAuthenticationInputModel>(m => m.RedirectUris.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_ChangingGrantTypes_PersistsNewSelection()
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientAuthenticationAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAuthentication());
        mock.Setup(s => s.UpdateClientAuthenticationAsync(ClientId.Create("test-client"), It.IsAny<ClientAuthenticationInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        var httpClient = CreateClient(mock.Object, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Authentication/test-client");

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

        var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        mock.Verify(s => s.UpdateClientAuthenticationAsync(
            ClientId.Create("test-client"),
            It.Is<ClientAuthenticationInputModel>(m => m.GrantTypes.Single() == "client_credentials" && !m.RequirePkce),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_NoGrantTypesSelected_ReturnsValidationError()
    {
        var httpClient = CreateClient(MockService(), allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Authentication/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("true"), "Input.RequirePkce");
        content.Add(new StringContent("true"), "Input.RequireClientSecret");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Authentication/test-client")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await GetDocumentAsync(response);
        var summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);
        Assert.Contains("grant type", summary!.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_MalformedRedirectUri_ReturnsValidationErrorWithUrisTabActive()
    {
        var httpClient = CreateClient(MockService(), allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Authentication/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("authorization_code"), "Input.GrantTypes");
        content.Add(new StringContent("not-a-url"), "Input.RedirectUris");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Authentication/test-client")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await GetDocumentAsync(response);
        var activeTab = document.QuerySelector("#client-authentication-tabs > ck-tab[active]");
        Assert.NotNull(activeTab);
        Assert.Equal("Redirect & CORS URIs", activeTab!.GetAttribute("label"));
        Assert.Contains("absolute HTTP or HTTPS URL", document.QuerySelector(".validation-summary")!.TextContent);
    }

    [Fact]
    public async Task Post_OverlongAuthenticationValues_ReturnsFieldErrorsAndDoesNotCallMutationService()
    {
        var mock = new Mock<IClientDetailsService>();
        mock.Setup(s => s.GetClientAuthenticationAsync(ClientId.Create("test-client"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAuthentication());

        var httpClient = CreateClient(mock.Object, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Authentication/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent(new string('g', ValidationConstants.MaxGrantTypeLength + 1)), "Input.GrantTypes");
        content.Add(new StringContent("https://example.com/" + new string('r', ValidationConstants.MaxClientRedirectUriLength)), "Input.RedirectUris");
        content.Add(new StringContent("https://example.com/" + new string('p', ValidationConstants.MaxClientPostLogoutRedirectUriLength)), "Input.PostLogoutRedirectUris");
        content.Add(new StringContent("https://example.com/" + new string('c', ValidationConstants.MaxClientCorsOriginLength)), "Input.CorsOrigins");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Authentication/test-client")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await GetDocumentAsync(response);
        var summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);
        Assert.Contains("Grant types cannot exceed", summary!.TextContent);
        Assert.Contains("absolute HTTP or HTTPS URL", summary.TextContent);
        var activeTab = document.QuerySelector("#client-authentication-tabs > ck-tab[active]");
        Assert.Equal("Redirect & CORS URIs", activeTab?.GetAttribute("label"));
        mock.Verify(
            service => service.UpdateClientAuthenticationAsync(
                It.IsAny<ClientId>(), It.IsAny<ClientAuthenticationInputModel>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Post_NonExistentClient_ReturnsNotFound()
    {
        var httpClient = CreateClient(MockService(), allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Clients/Authentication/test-client");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("authorization_code"), "Input.GrantTypes");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Clients/Authentication/non-existent")
        {
            Content = content
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}
