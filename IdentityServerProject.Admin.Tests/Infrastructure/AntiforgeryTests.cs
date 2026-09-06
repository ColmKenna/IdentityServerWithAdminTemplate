using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

/// <summary>
///     Razor Pages validates antiforgery on POST without anyone asking, so nothing in this
///     solution states it and — until now — nothing proved it. Every other integration test
///     supplies a token, which means the suite would pass unchanged if validation were turned
///     off. These assert the refusal, so the protection is evidenced rather than assumed.
/// </summary>
public class AntiforgeryTests : IClassFixture<AdminWebFactory>
{
    private const string CreatePage = "/Admin/Clients/Create";
    private readonly AdminWebFactory _factory;

    public AntiforgeryTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AdminPost_WithoutAnAntiforgeryToken_IsRefused()
    {
        HttpClient client = CreateAdminClient();

        HttpResponseMessage response = await client.PostAsync(CreatePage, ForgedForm());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminPost_WithAStolenTokenButNoMatchingCookie_IsRefused()
    {
        // The half that matters. A token lifted from a rendered page is worthless without
        // the cookie it is bound to; if only the form field were checked, a cross-site post
        // carrying a scraped token would be accepted.
        //
        // Two clients, deliberately: WebApplicationFactory's client keeps a cookie container,
        // so reading the token and posting it from the same one would carry the matching
        // cookie automatically and assert nothing.
        using HttpClient reader = CreateAdminClient();
        string token = await ExtractTokenAsync(reader, CreatePage);

        using HttpClient poster = CreateAdminClient();
        Dictionary<string, string> form = ForgedFields();
        form["__RequestVerificationToken"] = token;

        HttpResponseMessage response = await poster.PostAsync(CreatePage, new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheCreatePageStillRendersATokenToPostWith()
    {
        // Guards the two above from passing for the wrong reason: if the page stopped
        // rendering a form, every POST would fail and the refusals would prove nothing.
        HttpClient client = CreateAdminClient();

        string token = await ExtractTokenAsync(client, CreatePage);

        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    private static FormUrlEncodedContent ForgedForm() => new(ForgedFields());

    private static Dictionary<string, string> ForgedFields() => new()
    {
        ["Input.ClientId"] = "forged-client",
        ["Input.ClientName"] = "Forged Client"
    };

    private HttpClient CreateAdminClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> ExtractTokenAsync(HttpClient client, string url)
    {
        HttpResponseMessage response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        IDocument document = await context.OpenAsync(req => req.Content(html));

        var input = document.QuerySelector("input[name='__RequestVerificationToken']") as IHtmlInputElement;
        Assert.NotNull(input);

        return input.Value;
    }
}
