using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

/// <summary>
///     The authentication cookie's transport and lifetime are decisions the template makes on
///     a consumer's behalf, so they are asserted rather than left to whatever the framework
///     happens to default to. This reads the resolved options: the integration suite signs in
///     through a test scheme, so the real cookie is never issued during a test run and there
///     is no Set-Cookie header to inspect instead.
/// </summary>
public class CookieHardeningTests : IClassFixture<AdminWebFactory>
{
    private readonly CookieAuthenticationOptions _options;

    public CookieHardeningTests(AdminWebFactory factory)
    {
        _options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
    }

    [Fact]
    public void TheCookieIsHttpOnlyAndSecureOnly()
    {
        Assert.True(_options.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, _options.Cookie.SecurePolicy);
    }

    [Fact]
    public void SameSiteIsNoneBecauseIdentityServerNeedsTheCookieCrossSite()
    {
        // Not this project's choice: IdentityServer post-configures it for front-channel
        // logout and check-session. Pinned so that if the integration ever stops doing it,
        // this fails rather than the Secure requirement below quietly becoming optional.
        Assert.Equal(SameSiteMode.None, _options.Cookie.SameSite);
    }

    [Fact]
    public void TheLifetimeIsStatedRatherThanInherited()
    {
        Assert.Equal(TimeSpan.FromDays(14), _options.ExpireTimeSpan);
        Assert.True(_options.SlidingExpiration);
    }

    [Fact]
    public void TheSignInPathsAreUnchanged()
    {
        Assert.Equal("/Account/Login", _options.LoginPath);
        Assert.Equal("/Account/Logout", _options.LogoutPath);
        Assert.Equal("/Account/AccessDenied", _options.AccessDeniedPath);
    }
}
