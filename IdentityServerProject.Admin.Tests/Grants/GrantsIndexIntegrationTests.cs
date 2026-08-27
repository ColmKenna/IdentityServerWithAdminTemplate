using System.Net;
using AngleSharp;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Grants;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Grants;

public class GrantsIndexIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    private HttpClient CreateClient(IGrantListService grantListService)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(grantListService);
            });
        });
        _disposables.Add(factory);

        return factory.CreateClient();
    }

    private static IGrantListService MockService(ListResult<GrantListItem> result)
    {
        var mock = new Mock<IGrantListService>();
        mock.Setup(s => s.GetGrantsAsync(It.IsAny<GrantFilter?>(), It.IsAny<Pagination>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static ListResult<GrantListItem> EmptyResult(int pageNumber = 1, int pageSize = 10) => new()
    {
        Items = Array.Empty<GrantListItem>(),
        TotalCount = 0,
        PageNumber = pageNumber,
        PageSize = pageSize,
    };

    private static GrantListItem MakeItem(string key, string clientId, string clientName, string subjectId) => new()
    {
        Key = GrantKey.Create(key),
        Type = "user_consent",
        SubjectId = UserId.Create(subjectId),
        SessionId = "session-1",
        ClientId = ClientId.Create(clientId),
        ClientName = clientName,
        Description = "Consent grant",
        CreationTime = DateTime.UtcNow.AddDays(-1),
        Expiration = DateTime.UtcNow.AddDays(6),
        ExpirationFormatted = "in 6 days",
        IsExpired = false
    };

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    [Fact]
    public async Task GetIndex_Returns200_AndRendersGrantsTable()
    {
        var item1 = MakeItem("grant-key-1", "client-1", "Client One", "user-101");
        var item2 = MakeItem("grant-key-2", "client-2", "Client Two", "user-102");

        var service = MockService(new ListResult<GrantListItem>
        {
            Items = new[] { item1, item2 },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 10
        });

        var client = CreateClient(service);

        var response = await client.GetAsync("/Admin/Grants");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(content));

        Assert.NotNull(document.QuerySelector("h1.page-title"));
        Assert.Equal("Persisted Grants", document.QuerySelector("h1.page-title")?.TextContent?.Trim());

        var rows = document.QuerySelectorAll("ck-responsive-row");
        Assert.Equal(2, rows.Length);

        Assert.Contains("grant-key-1", rows[0].TextContent);
        Assert.Contains("Client One", rows[0].TextContent);
        Assert.Contains("user-101", rows[0].TextContent);

        Assert.Contains("grant-key-2", rows[1].TextContent);
        Assert.Contains("Client Two", rows[1].TextContent);
        Assert.Contains("user-102", rows[1].TextContent);
    }

    [Fact]
    public async Task GetIndex_WhenNoItems_RendersEmptyState()
    {
        var service = MockService(EmptyResult());
        var client = CreateClient(service);

        var response = await client.GetAsync("/Admin/Grants");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(content));

        var emptyState = document.QuerySelector(".empty-state");
        Assert.NotNull(emptyState);
        Assert.Equal("No persisted grants found.", emptyState?.TextContent?.Trim());
    }

    [Fact]
    public async Task GetIndex_RendersGrantFiltersAndPreservesThemInNavigationAndRevocation()
    {
        var service = MockService(new ListResult<GrantListItem>
        {
            Items = Array.Empty<GrantListItem>(),
            TotalCount = 30,
            PageNumber = 2,
            PageSize = 10,
        });
        var client = CreateClient(service);

        var response = await client.GetAsync("/Admin/Grants?SubjectId=user-101&ClientId=client-1&TypeFilter=refresh_token&PageNumber=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(content));

        Assert.NotNull(document.QuerySelector("ck-responsive-table"));
        Assert.Equal("user-101", document.QuerySelector("input[name=SubjectId]")?.GetAttribute("value"));
        Assert.Equal("client-1", document.QuerySelector("input[name=ClientId]")?.GetAttribute("value"));
        Assert.Equal("refresh_token", document.QuerySelector("input[name=TypeFilter]")?.GetAttribute("value"));
        var previousLink = document.QuerySelector("a[data-pagination=previous]")?.GetAttribute("href");
        Assert.Contains("SubjectId=user-101", previousLink);
        Assert.Contains("ClientId=client-1", previousLink);
        Assert.Contains("TypeFilter=refresh_token", previousLink);
        Assert.NotNull(document.QuerySelector("#revoke-grant-modal"));
        Assert.Equal("user-101", document.QuerySelector("#revoke-grant-modal input[name=SubjectId]")?.GetAttribute("value"));
        Assert.Equal("client-1", document.QuerySelector("#revoke-grant-modal input[name=ClientId]")?.GetAttribute("value"));
        Assert.Equal("refresh_token", document.QuerySelector("#revoke-grant-modal input[name=TypeFilter]")?.GetAttribute("value"));
    }
}
