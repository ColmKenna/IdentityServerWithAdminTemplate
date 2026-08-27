using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.IdentityResources;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.IdentityResources;

public class IdentityResourcesIndexIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private HttpClient CreateClient(IIdentityResourceListService identityResourceListService)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => { services.AddSingleton(identityResourceListService); });
        });
        _disposables.Add(factory);

        return factory.CreateClient();
    }

    private static IIdentityResourceListService MockService(ListResult<IdentityResourceListItem> result)
    {
        var mock = new Mock<IIdentityResourceListService>();
        mock.Setup(s => s.GetIdentityResourcesAsync(It.IsAny<ListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static ListResult<IdentityResourceListItem> EmptyResult(int pageNumber = 1, int pageSize = 10) => new()
    {
        Items = Array.Empty<IdentityResourceListItem>(),
        TotalCount = 0,
        PageNumber = pageNumber,
        PageSize = pageSize
    };

    private static IdentityResourceListItem MakeItem(string name, string displayName, bool enabled = true,
        int claimsCount = 1, int refCount = 0) => new()
    {
        Name = name,
        DisplayName = displayName,
        Description = $"{displayName} description",
        Enabled = enabled,
        Required = true,
        Emphasize = false,
        ShowInDiscoveryDocument = true,
        UserClaimsCount = claimsCount,
        ClientReferenceCount = refCount,
        NonEditable = false
    };

    [Fact]
    public async Task GetIndex_Returns200_AndRendersIdentityResourcesTable()
    {
        IdentityResourceListItem item1 = MakeItem("openid", "OpenID", true, 1, 2);
        IdentityResourceListItem item2 = MakeItem("profile", "User profile", true, 4, 1);

        IIdentityResourceListService service = MockService(new ListResult<IdentityResourceListItem>
        {
            Items = new[] { item1, item2 },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 10
        });

        HttpClient client = CreateClient(service);

        HttpResponseMessage response = await client.GetAsync("/Admin/IdentityResources");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        IDocument document = await context.OpenAsync(req => req.Content(content));

        Assert.NotNull(document.QuerySelector("h1.page-title"));
        Assert.Equal("Identity Resources", document.QuerySelector("h1.page-title")?.TextContent?.Trim());

        IHtmlCollection<IElement> rows = document.QuerySelectorAll("ck-responsive-row");
        Assert.Equal(2, rows.Length);

        Assert.Contains("openid", rows[0].TextContent);
        Assert.Contains("profile", rows[1].TextContent);
    }

    [Fact]
    public async Task GetIndex_WhenNoItems_RendersEmptyState()
    {
        IIdentityResourceListService service = MockService(EmptyResult());
        HttpClient client = CreateClient(service);

        HttpResponseMessage response = await client.GetAsync("/Admin/IdentityResources");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        IDocument document = await context.OpenAsync(req => req.Content(content));

        IElement? emptyState = document.QuerySelector(".empty-state");
        Assert.NotNull(emptyState);
        Assert.Equal("No identity resources found.", emptyState?.TextContent?.Trim());
    }

    [Fact]
    public async Task GetIndex_RendersPageHeaderAndSearchInput()
    {
        IIdentityResourceListService service = MockService(EmptyResult());
        HttpClient client = CreateClient(service);

        HttpResponseMessage response = await client.GetAsync("/Admin/IdentityResources");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        IDocument document = await context.OpenAsync(req => req.Content(content));

        Assert.NotNull(document.QuerySelector("ck-responsive-table"));
        Assert.NotNull(document.QuerySelector("#delete-resource-modal"));
    }
}