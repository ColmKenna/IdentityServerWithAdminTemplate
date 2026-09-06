using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

/// <summary>
///     The fallback policy makes an endpoint that declares no authorisation deny rather than
///     serve, so a page added outside /Admin fails closed instead of open. Both directions
///     are covered here: the unattributed page is refused, and each page that has to stay
///     anonymous still answers — over-locking the login page is the way this change breaks.
/// </summary>
public class FallbackAuthorizationTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public FallbackAuthorizationTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AnonymousRequest_ToAPageDeclaringNoAuthorisation_IsSentToLogin()
    {
        // Consent carries no attribute and no convention. Without the fallback it answers
        // anonymously — redirecting to /Error because there is no authorization context —
        // so the destination of this redirect is what the fallback actually changes.
        HttpClient client = CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync("/Consent");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Logout")]
    [InlineData("/Account/AccessDenied")]
    public async Task AnonymousRequest_ToAnExemptedAccountPage_IsStillAnswered(string path)
    {
        HttpClient client = CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousRequest_ToTheRoot_StillRedirectsToTheConsole()
    {
        // The root stays anonymous on purpose: challenging here would send the login page
        // returnUrl=/ instead of returnUrl=/Admin, and the console is where the user is going.
        HttpClient client = CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin", response.Headers.Location?.OriginalString);
    }

    private HttpClient CreateAnonymousClient()
    {
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-Auth", "anonymous");
        return client;
    }
}
