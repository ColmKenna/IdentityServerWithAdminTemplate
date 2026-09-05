using IdentityServerProject.Pages.Shared;

namespace IdentityServerProject.Admin.Tests.Shared;

/// <summary>
///     The route-state and trail arithmetic the admin layout used to do inline.
/// </summary>
public class LayoutPresentationStateTests
{
    [Theory]
    [InlineData("/Admin/Clients/Index", "/Admin/Clients", true)]
    [InlineData("/Admin/Clients/Details", "/Admin/Clients", true)]
    [InlineData("/Admin/Clients", "/Admin/Clients", true)]
    // Segment boundaries: a plain StartsWith would light both of these up.
    [InlineData("/Admin/ApiScopes/Index", "/Admin/Apis", false)]
    [InlineData("/Admin/Apis/Index", "/Admin/ApiScopes", false)]
    [InlineData("/Admin/ApiScopes/Index", "/Admin/ApiScopes", true)]
    [InlineData("/admin/clients/index", "/Admin/Clients", true)]
    [InlineData("/Admin/Index", "/Admin/Clients", false)]
    [InlineData("", "/Admin/Clients", false)]
    public void IsActiveSection_MatchesOnSegmentBoundaries(string currentPage, string prefix, bool expected)
    {
        var nav = new AdminNavigationState(currentPage);

        Assert.Equal(expected, nav.IsActiveSection(prefix));
        Assert.Equal(expected ? "nav-item active" : "nav-item", nav.ItemClass(prefix));
    }

    [Fact]
    public void IsActiveSection_WithNoRoutePage_MatchesNothing()
    {
        var nav = new AdminNavigationState(null);

        Assert.False(nav.IsActiveSection("/Admin/Clients"));
        Assert.Equal("nav-item", nav.ItemClass("/Admin/Clients"));
    }

    [Fact]
    public void Resolve_LinksEveryCrumbButTheLast_AndSeparatesAllButTheFirst()
    {
        IReadOnlyList<BreadcrumbTrailItem> trail = BreadcrumbTrail.Resolve(new List<Breadcrumb>
        {
            new("Admin", "/Admin/Index"),
            new("Users", "/Admin/Users/Index"),
            new("jane")
        });

        Assert.Equal(new[] { false, true, true }, trail.Select(item => item.NeedsSeparator));
        Assert.Equal(new[] { true, true, false }, trail.Select(item => item.IsLink));
        Assert.Equal(new[] { "Admin", "Users", "jane" }, trail.Select(item => item.Text));
    }

    [Fact]
    public void Resolve_LastCrumb_IsNeverALinkEvenWhenItNamesAPage()
    {
        IReadOnlyList<BreadcrumbTrailItem> trail = BreadcrumbTrail.Resolve(new List<Breadcrumb>
        {
            new("Admin", "/Admin/Index"),
            new("Clients", "/Admin/Clients/Index")
        });

        Assert.True(trail[0].IsLink);
        Assert.False(trail[1].IsLink);
        Assert.Equal("/Admin/Clients/Index", trail[1].Page);
    }

    [Fact]
    public void Resolve_CarriesRouteValuesThrough()
    {
        var routeValues = new Dictionary<string, string> { ["id"] = "42" };

        IReadOnlyList<BreadcrumbTrailItem> trail = BreadcrumbTrail.Resolve(new List<Breadcrumb>
        {
            new("Client", "/Admin/Clients/Details", routeValues),
            new("Secrets")
        });

        Assert.Same(routeValues, trail[0].RouteValues);
        Assert.Null(trail[1].RouteValues);
    }

    [Fact]
    public void Resolve_SingleCrumb_IsPlainTextWithNoSeparator()
    {
        IReadOnlyList<BreadcrumbTrailItem> trail =
            BreadcrumbTrail.Resolve(new List<Breadcrumb> { new("Admin") });

        BreadcrumbTrailItem only = Assert.Single(trail);
        Assert.False(only.IsLink);
        Assert.False(only.NeedsSeparator);
    }

    [Fact]
    public void Resolve_EmptyTrail_YieldsNothing()
    {
        Assert.Empty(BreadcrumbTrail.Resolve(Array.Empty<Breadcrumb>()));
    }
}
