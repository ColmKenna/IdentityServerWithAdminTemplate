using IdentityServerProject.Pages.Shared;
using Microsoft.AspNetCore.Http;

namespace IdentityServerProject.Admin.Tests.Shared;

/// <summary>
///     The previous/next link route values the pagination partial used to assemble inline.
/// </summary>
public class PaginationRouteTests
{
    private static PaginationModel Model(
        int pageNumber = 2,
        int totalPages = 3,
        IDictionary<string, string?>? routeValues = null) =>
        new(
            currentCount: 10,
            totalCount: 30,
            pageNumber: pageNumber,
            totalPages: totalPages,
            hasPreviousPage: pageNumber > 1,
            hasNextPage: pageNumber < totalPages,
            noun: "clients",
            routeValues: routeValues);

    private static IQueryCollection Query(params (string Key, string Value)[] entries) =>
        new QueryCollection(entries.ToDictionary(e => e.Key, e => new Microsoft.Extensions.Primitives.StringValues(e.Value)));

    [Fact]
    public void ExplicitRouteValues_AreCarriedOntoBothLinks()
    {
        PaginationModel model = Model(routeValues: new Dictionary<string, string?> { ["Filter"] = "acme" });

        Assert.Equal("acme", model.PreviousPageRoute(null)!["Filter"]);
        Assert.Equal("1", model.PreviousPageRoute(null)!["PageNumber"]);
        Assert.Equal("acme", model.NextPageRoute(null)!["Filter"]);
        Assert.Equal("3", model.NextPageRoute(null)!["PageNumber"]);
    }

    [Fact]
    public void WithoutExplicitRouteValues_TheCurrentQueryIsCarriedInstead()
    {
        PaginationModel model = Model();
        IQueryCollection query = Query(("Filter", "acme"), ("Sort", "name"), ("PageNumber", "2"));

        IDictionary<string, string?> next = model.NextPageRoute(query)!;

        Assert.Equal("acme", next["Filter"]);
        Assert.Equal("name", next["Sort"]);
        // Replaced, never appended twice.
        Assert.Equal("3", next["PageNumber"]);
    }

    [Fact]
    public void ExplicitRouteValues_SuppressTheQueryStringFallback()
    {
        PaginationModel model = Model(routeValues: new Dictionary<string, string?> { ["Filter"] = "explicit" });

        IDictionary<string, string?> next = model.NextPageRoute(Query(("Sort", "name")))!;

        Assert.Equal("explicit", next["Filter"]);
        Assert.False(next.ContainsKey("Sort"));
    }

    [Fact]
    public void EmptyValues_AreDroppedFromExplicitValuesAndFromTheQuery()
    {
        PaginationModel withBlankExplicit =
            Model(routeValues: new Dictionary<string, string?> { ["Filter"] = "", ["Sort"] = "name" });
        Assert.False(withBlankExplicit.NextPageRoute(null)!.ContainsKey("Filter"));

        PaginationModel fromQuery = Model();
        Assert.False(fromQuery.NextPageRoute(Query(("Filter", "")))!.ContainsKey("Filter"));
    }

    [Fact]
    public void PageNumberIsNeverHarvestedFromTheQueryUnderAnotherCasing()
    {
        PaginationModel model = Model();

        IDictionary<string, string?> next = model.NextPageRoute(Query(("pagenumber", "2"), ("Filter", "acme")))!;

        Assert.Equal("3", next["PageNumber"]);
        Assert.False(next.ContainsKey("pagenumber"));
    }

    [Fact]
    public void FirstAndLastPages_HaveNoRouteForTheDisabledDirection()
    {
        PaginationModel first = Model(pageNumber: 1, totalPages: 3);
        Assert.Null(first.PreviousPageRoute(null));
        Assert.NotNull(first.NextPageRoute(null));

        PaginationModel last = Model(pageNumber: 3, totalPages: 3);
        Assert.NotNull(last.PreviousPageRoute(null));
        Assert.Null(last.NextPageRoute(null));

        PaginationModel only = Model(pageNumber: 1, totalPages: 1);
        Assert.Null(only.PreviousPageRoute(null));
        Assert.Null(only.NextPageRoute(null));
    }

    [Fact]
    public void NoRouteValuesAndNoQuery_StillNumbersThePage()
    {
        IDictionary<string, string?> next = Model().NextPageRoute(null)!;

        Assert.Equal("3", Assert.Single(next).Value);
    }
}
