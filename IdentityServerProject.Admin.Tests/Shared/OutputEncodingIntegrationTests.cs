using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Shared;

/// <summary>
///     Guards the encoding boundary in the two shared partials that render developer-authored
///     markup through Html.Raw: _ConfirmationModal and _SecretRevealBanner.
/// </summary>
/// <remarks>
///     Both partials deliberately emit their body unencoded so the &lt;strong&gt; and &lt;code&gt;
///     tags written into the literal render as markup. Runtime values must not travel that path;
///     they are passed as composite-format arguments and encoded by SafeMarkupBody.
///     <para>
///         Each test asserts three things, and the third is the one that stops an over-correction:
///         the runtime value survives as <em>text</em>, it produces no element, and the literal
///         markup around it still renders. Encoding the whole body would pass the first two and
///         fail the third.
///     </para>
///     <para>
///         Names and ids here contain markup because nothing validates them at this layer —
///         ClientId.Create only trims, and the API resource name is seeded straight into the
///         store. Whether such a value should be creatable through the UI is a separate question;
///         these tests cover what the view does when it meets one.
///     </para>
/// </remarks>
public class OutputEncodingIntegrationTests : IDisposable
{
    // No "/" in the payload: ASP.NET Core deliberately does not decode %2F inside a path
    // segment, so a closing tag would arrive at the page as literal "%2F" and the test would
    // be asserting against a value the app never really saw. This shape is a real XSS probe
    // and round-trips through both a query string and a route segment unchanged.
    private const string Injected = "<img src=x onerror=alert(1)>";

    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
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
    public async Task ConfirmationModal_ResourceNameContainingMarkup_IsEncodedButKeepsLiteralMarkup()
    {
        var factory = new AdminWebFactory();
        _disposables.Add(factory);
        HttpClient client = factory.CreateClient();

        string name = $"{Guid.NewGuid():N}-{Injected}-api";
        await factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiResources.Add(new ApiResource { Name = name, Enabled = true });
            await configDb.SaveChangesAsync();
        });

        HttpResponseMessage response =
            await client.GetAsync($"/Admin/Apis/Editor?name={Uri.EscapeDataString(name)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);
        IElement? body = document.QuerySelector("#delete-modal-desc");
        Assert.NotNull(body);

        // The resource name reaches the page as text, exactly as stored.
        Assert.Contains(name, body!.TextContent);

        // It did not become an element — this is the assertion that fails if Html.Raw returns.
        Assert.Null(body.QuerySelector("img"));

        // The <strong> the body literal asks for is still markup, so this is encoding the
        // argument rather than encoding everything.
        Assert.NotEmpty(body.QuerySelectorAll("strong"));
    }

    [Fact]
    public async Task SecretRevealBanner_ClientIdContainingMarkup_IsEncodedButKeepsLiteralMarkup()
    {
        string clientId = $"{Guid.NewGuid():N}-{Injected}-client";

        var mock = new Mock<IClientSecretsService>();
        mock.Setup(s => s.GetClientSecretsAsync(It.IsAny<ClientId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientSecretsModel
            {
                ClientId = ClientId.Create(clientId),
                ClientName = "Co-op Market Razor Client",
                RequireClientSecret = true,
                Secrets = new List<ClientSecretSummary>
                {
                    new() { Id = 1, Description = "Secret 1", Created = DateTime.UtcNow.AddDays(-1) }
                }
            });
        mock.Setup(s => s.GenerateClientSecretAsync(It.IsAny<ClientId>(), It.IsAny<string>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientSecretGenerateResult.Succeeded("brand-new-plaintext-secret"));

        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);
        WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => services.AddSingleton(mock.Object));
        });
        _disposables.Add(factory);
        HttpClient httpClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = true
        });

        string url = $"/Admin/Clients/Secrets/{Uri.EscapeDataString(clientId)}";
        (string token, string cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, url);

        var content = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent("Rotated secret"), "Description" }
        };
        var request = new HttpRequestMessage(HttpMethod.Post, $"{url}?handler=Generate") { Content = content };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);
        IElement? banner = document.QuerySelector("#secret-reveal-banner");
        Assert.NotNull(banner);

        Assert.Contains(clientId, banner!.TextContent);
        Assert.Null(banner.QuerySelector("img"));
        Assert.NotEmpty(banner.QuerySelectorAll("code"));
    }
}
