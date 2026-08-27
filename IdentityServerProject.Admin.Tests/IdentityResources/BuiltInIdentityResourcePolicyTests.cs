using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.IdentityResources;

namespace IdentityServerProject.Admin.Tests.IdentityResources;

/// <summary>
///     Unit coverage for the protection rules themselves, independent of any store. The service tests
///     prove the rules are applied; these prove the rules are right.
/// </summary>
public class BuiltInIdentityResourcePolicyTests
{
    [Theory]
    [InlineData("openid")]
    [InlineData("OpenId")]
    [InlineData("OPENID")]
    [InlineData("  openid  ")]
    public void IsProtectedName_OpenIdInAnyCasing_ReturnsTrue(string name)
    {
        // Resource names round-trip through query strings and form posts, so casing and stray
        // whitespace are both reachable from a hand-crafted request.
        Assert.True(BuiltInIdentityResourcePolicy.IsProtectedName(name));
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("email")]
    [InlineData("roles")]
    [InlineData("custom.resource")]
    [InlineData("openid2")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsProtectedName_EverythingElse_ReturnsFalse(string? name)
    {
        // profile, email, and roles are seeded but deliberately left editable: their claim sets are
        // operator-curated and trimming one breaks no protocol invariant.
        Assert.False(BuiltInIdentityResourcePolicy.IsProtectedName(name));
    }

    [Fact]
    public void IsProtected_HonoursTheNonEditableFlagOnAnOrdinaryResource()
    {
        var entity = new IdentityResource { Name = "custom.resource", NonEditable = true };

        Assert.True(BuiltInIdentityResourcePolicy.IsProtected(entity));
    }

    [Fact]
    public void IsProtected_HonoursTheNameEvenWhenTheFlagIsUnset()
    {
        // The case that matters for existing databases: every row seeded before this policy landed
        // carries NonEditable = false, and EnsureCreated leaves no migration path to fix them.
        var entity = new IdentityResource { Name = "openid", NonEditable = false };

        Assert.True(BuiltInIdentityResourcePolicy.IsProtected(entity));
    }

    [Fact]
    public void IsProtected_OrdinaryEditableResource_ReturnsFalse()
    {
        var entity = new IdentityResource { Name = "custom.resource", NonEditable = false };

        Assert.False(BuiltInIdentityResourcePolicy.IsProtected(entity));
    }

    [Theory]
    [InlineData("openid", "sub")]
    [InlineData("OPENID", "SUB")]
    [InlineData(" openid ", " sub ")]
    public void IsInvariantClaim_SubOnOpenId_ReturnsTrue(string resourceName, string claimType) =>
        Assert.True(BuiltInIdentityResourcePolicy.IsInvariantClaim(resourceName, claimType));

    [Theory]
    [InlineData("openid", "email")]
    [InlineData("profile", "sub")]
    [InlineData("custom.resource", "sub")]
    [InlineData(null, "sub")]
    [InlineData("openid", null)]
    public void IsInvariantClaim_EverythingElse_ReturnsFalse(string? resourceName, string? claimType)
    {
        // Notably "profile"/"sub": the invariant is scoped to a resource, not to the claim type
        // globally, so an operator can still curate sub out of their own resources.
        Assert.False(BuiltInIdentityResourcePolicy.IsInvariantClaim(resourceName, claimType));
    }

    [Fact]
    public void Messages_NameTheResourceAndClaimSoTheOperatorKnowsWhatWasRefused()
    {
        Assert.Contains("openid", BuiltInIdentityResourcePolicy.ProtectedMessage("openid"));

        string invariant = BuiltInIdentityResourcePolicy.InvariantClaimMessage("openid", "sub");
        Assert.Contains("openid", invariant);
        Assert.Contains("sub", invariant);
    }
}