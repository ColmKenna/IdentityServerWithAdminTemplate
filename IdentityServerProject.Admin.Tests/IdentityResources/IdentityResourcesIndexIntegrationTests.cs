using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using IdentityServerProject.Services;
using IdentityServerProject.Services.IdentityResources;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.IdentityResources;

public class IdentityResourcesIndexIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    private HttpClient CreateClient(IIdentityResourceListService identityResourceListService)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(identityResourceListService);
            });
        });
        _disposables.Add(factory);

        return factory.CreateClient();
    }

    private static IIdentityResourceListService MockService(ListResult<IdentityResourceListItem> result)
    {
        var mock = new Mock<IIdentityResourceListService>();
        mock.Setup(s => s.GetIdentityResourcesAsync(It.IsAny<string?>(), It.IsAny<Pagination>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static ListResult<IdentityResourceListItem> EmptyResult(int pageNumber = 1, int pageSize = 10) => new()
    {
        Items = Array.Empty<IdentityResourceListItem>(),
        TotalCount = 0,
        PageNumber = pageNumber,
        PageSize = pageSize,
    };

    private static IdentityResourceListItem MakeItem(string name, string displayName, bool enabled = true, int claimsCount = 1, int refCount = 0) => new()
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

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    [Fact]
    public async Task GetIndex_Returns200_AndRendersIdentityResourcesTable()
    {
        var item1 = MakeItem("openid", "OpenID", enabled: true, claimsCount: 1, refCount: 2);
        var item2 = MakeItem("profile", "User profile", enabled: true, claimsCount: 4, refCount: 1);

        var service = MockService(new ListResult<IdentityResourceListItem>
        {
            Items = new[] { item1, item2 },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 10
        });

        var client = CreateClient(service);

        var response = await client.GetAsync("/Admin/IdentityResources");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(content));

        Assert.NotNull(document.QuerySelector("h1.page-title"));
        Assert.Equal("Identity Resources", document.QuerySelector("h1.page-title")?.TextContent?.Trim());

        var rows = document.QuerySelectorAll("ck-responsive-row");
        Assert.Equal(2, rows.Length);

        Assert.Contains("openid", rows[0].TextContent);
        Assert.Contains("profile", rows[1].TextContent);
    }

    [Fact]
    public async Task GetIndex_WhenNoItems_RendersEmptyState()
    {
        var service = MockService(EmptyResult());
        var client = CreateClient(service);

        var response = await client.GetAsync("/Admin/IdentityResources");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(content));

        var emptyState = document.QuerySelector(".empty-state");
        Assert.NotNull(emptyState);
        Assert.Equal("No identity resources found.", emptyState?.TextContent?.Trim());
    }

    [Fact]
    public async Task GetIndex_RendersPageHeaderAndSearchInput()
    {
        var service = MockService(EmptyResult());
        var client = CreateClient(service);

        var response = await client.GetAsync("/Admin/IdentityResources");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(content));

        Assert.NotNull(document.QuerySelector("ck-responsive-table"));
        Assert.NotNull(document.QuerySelector("#delete-resource-modal"));
    }
}
