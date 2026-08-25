using System.Collections.Generic;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientEndpointsTests
{
    [Theory]
    [InlineData("https://example.com/callback", true)]
    [InlineData("http://localhost:5000/signin", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("not-a-url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AbsoluteHttpUri_TryCreate_ValidatesCorrectly(string? input, bool expectedSuccess)
    {
        var success = AbsoluteHttpUri.TryCreate(input, ValidationConstants.MaxClientRedirectUriLength, out var uri, out var error);
        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Null(error);
            Assert.Equal(input!.Trim(), uri.Value);
            Assert.Equal(input.Trim(), (string)uri);
        }
        else
        {
            Assert.NotNull(error);
        }
    }

    [Theory]
    [InlineData("https://example.com:8443/", true, "https://example.com:8443")]
    [InlineData("http://localhost:3000", true, "http://localhost:3000")]
    [InlineData("https://example.com:443/", true, "https://example.com")]
    [InlineData("https://example.com/path", false, null)]
    [InlineData("ftp://example.com", false, null)]
    [InlineData("", false, null)]
    public void CorsOrigin_TryCreate_ValidatesAndNormalizes(string? input, bool expectedSuccess, string? expectedNormalized)
    {
        var success = CorsOrigin.TryCreate(input, ValidationConstants.MaxClientCorsOriginLength, out var origin, out var error);
        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Null(error);
            Assert.Equal(expectedNormalized, origin.Value);
            Assert.Equal(expectedNormalized, (string)origin);
        }
        else
        {
            Assert.NotNull(error);
        }
    }

    [Fact]
    public void ClientEndpoints_Normalize_TrimsAndDeduplicates()
    {
        var endpoints = new ClientEndpoints(
            redirectUris: new List<string> { " https://example.com/a ", "https://example.com/a", "" },
            postLogoutRedirectUris: new List<string> { " https://example.com/out ", "https://example.com/out" },
            allowedCorsOrigins: new List<string> { "https://example.com:443/", "https://example.com" },
            frontChannelLogoutUri: " https://example.com/front ",
            frontChannelLogoutSessionRequired: true,
            backChannelLogoutUri: " https://example.com/back ",
            backChannelLogoutSessionRequired: false);

        var normalized = endpoints.Normalize();

        Assert.Equal(new[] { "https://example.com/a" }, normalized.RedirectUris);
        Assert.Equal(new[] { "https://example.com/out" }, normalized.PostLogoutRedirectUris);
        Assert.Equal(new[] { "https://example.com" }, normalized.AllowedCorsOrigins);
        Assert.Equal("https://example.com/front", normalized.FrontChannelLogoutUri);
        Assert.True(normalized.FrontChannelLogoutSessionRequired);
        Assert.Equal("https://example.com/back", normalized.BackChannelLogoutUri);
        Assert.False(normalized.BackChannelLogoutSessionRequired);
    }
}
