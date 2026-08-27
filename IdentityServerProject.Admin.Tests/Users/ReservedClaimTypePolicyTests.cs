using System.Security.Claims;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IdentityServerProject.Admin.Tests.Users;

/// <summary>
/// Unit-tests the deny-list itself, independently of the service that consumes it. These are the
/// cases that decide whether an administrator can mint a privileged claim, so they are asserted
/// exhaustively rather than through the service round-trip.
/// </summary>
public class ReservedClaimTypePolicyTests
{
    private static ReservedClaimTypePolicy CreatePolicy(IdentityOptions? options = null) =>
        new(Options.Create(options ?? new IdentityOptions()));

    [Theory]
    // Short OIDC/JWT forms.
    [InlineData("role")]
    [InlineData("roles")]
    [InlineData("sub")]
    [InlineData("amr")]
    [InlineData("idp")]
    [InlineData("auth_time")]
    // WS-* URI forms, which are what ASP.NET Identity authorization actually reads by default.
    [InlineData("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")]
    [InlineData("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")]
    // Identity's own internal principal claims.
    [InlineData("AspNet.Identity.SecurityStamp")]
    [InlineData("AspNet.Identity.Anything")]
    public void IsReserved_FrameworkOwnedType_ReturnsTrue(string claimType)
    {
        Assert.True(CreatePolicy().IsReserved(claimType));
    }

    [Theory]
    // ClaimsIdentity compares claim types with OrdinalIgnoreCase, so casing must not be an escape.
    [InlineData("ROLE")]
    [InlineData("Role")]
    [InlineData("rOlEs")]
    [InlineData("aspnet.identity.securitystamp")]
    [InlineData("HTTP://SCHEMAS.MICROSOFT.COM/WS/2008/06/IDENTITY/CLAIMS/ROLE")]
    public void IsReserved_IsCaseInsensitive(string claimType)
    {
        Assert.True(CreatePolicy().IsReserved(claimType));
    }

    [Theory]
    // Surrounding whitespace must not produce a distinct-but-equivalent stored type.
    [InlineData(" role")]
    [InlineData("role ")]
    [InlineData("\trole\n")]
    public void IsReserved_IgnoresSurroundingWhitespace(string claimType)
    {
        Assert.True(CreatePolicy().IsReserved(claimType));
    }

    [Theory]
    // Ordinary profile/application claims must keep working. "name" in particular is seeded onto
    // every user by SeedData, and "email" is a standard profile claim.
    [InlineData("name")]
    [InlineData("email")]
    [InlineData("dept")]
    [InlineData("team")]
    [InlineData("given_name")]
    [InlineData("employee_number")]
    // Near-misses that are not the reserved type.
    [InlineData("role_group")]
    [InlineData("my_roles")]
    [InlineData("subject")]
    public void IsReserved_OrdinaryClaimType_ReturnsFalse(string claimType)
    {
        Assert.False(CreatePolicy().IsReserved(claimType));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsReserved_BlankType_ReturnsFalse(string? claimType)
    {
        // Blank is not "reserved" — it is invalid, and the service rejects it separately.
        Assert.False(CreatePolicy().IsReserved(claimType));
    }

    [Fact]
    public void IsReserved_HonoursConfiguredIdentityOptions()
    {
        var options = new IdentityOptions();
        options.ClaimsIdentity.RoleClaimType = "https://sales.local/claims/role";

        var policy = CreatePolicy(options);

        Assert.True(policy.IsReserved("https://sales.local/claims/role"));

        // The default is still blocked as well — overriding the option must not open the type the
        // rest of the framework (and any library reading ClaimTypes.Role directly) still honours.
        Assert.True(policy.IsReserved(ClaimTypes.Role));
    }

    [Fact]
    public void IsReserved_DisplayClaimTypes_StayAllowedUnderDuendesConfiguration()
    {
        // Duende's AddAspNetIdentity sets UserNameClaimType to "name", which SeedData writes onto
        // every user as an ordinary profile claim. Deriving the deny-list from that option would
        // block an established convention for no security gain, so it is excluded.
        var options = new IdentityOptions();
        options.ClaimsIdentity.UserIdClaimType = "sub";
        options.ClaimsIdentity.UserNameClaimType = "name";
        options.ClaimsIdentity.RoleClaimType = "role";

        var policy = CreatePolicy(options);

        Assert.False(policy.IsReserved("name"));
        Assert.False(policy.IsReserved("email"));
        Assert.True(policy.IsReserved("role"));
        Assert.True(policy.IsReserved("sub"));
    }

    [Fact]
    public void ReservedTypes_ExposesTheExactMatchListForDisplay()
    {
        var reservedTypes = CreatePolicy().ReservedTypes;

        Assert.Contains("role", reservedTypes);
        Assert.Contains(ClaimTypes.Role, reservedTypes);
        Assert.Contains(new IdentityOptions().ClaimsIdentity.SecurityStampClaimType, reservedTypes);
    }

    [Theory]
    [InlineData(" role ", "role")]
    [InlineData("dept", "dept")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalize_TrimsAndNullCoalesces(string? input, string expected)
    {
        Assert.Equal(expected, ReservedClaimTypePolicy.Normalize(input));
    }
}
