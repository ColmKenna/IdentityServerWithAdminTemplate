using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Clients;

/// <summary>
///     Full HTTP pipeline tests for GET /Admin/Clients, asserting on the rendered HTML via AngleSharp.
///     Each test builds its own factory/client with <see cref="IClientListService" /> replaced by a
///     mock so the rendered markup is fully controlled and independent of database state.
/// </summary>
public class ClientsIndexIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private HttpClient CreateClient(IClientListService clientListService)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => { services.AddSingleton(clientListService); });
        });
        _disposables.Add(factory);

        return factory.CreateClient();
    }

    private static IClientListService MockService(ListResult<ClientListItem> result)
    {
        var mock = new Mock<IClientListService>();
        mock.Setup(s => s.GetClientsAsync(It.IsAny<ListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static ListResult<ClientListItem> EmptyResult(int pageNumber = 1, int pageSize = 10) => new()
    {
        Items = Array.Empty<ClientListItem>(),
        TotalCount = 0,
        PageNumber = pageNumber,
        PageSize = pageSize
    };

    private static ClientListItem MakeItem(string suffix, bool enabled = true) => new()
    {
        ClientId = $"client-{suffix}",
        ClientName = $"Client {suffix}",
        ClientType = "Authorization Code",
        Enabled = enabled
    };

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    // ---------- Step 1: navigation shell & routing ----------

    [Fact]
    public async Task Get_ReturnsSuccessStatusCode()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()));

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_RendersAdminLayoutSidebarWithClientsNavItemActive()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()));

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients");
        IDocument document = await GetDocumentAsync(response);

        Assert.NotNull(document.QuerySelector("aside.sidebar"));
        IElement? navItem = document.QuerySelector("a[href*='/Admin/Clients']");
        Assert.NotNull(navItem);
        Assert.Contains("active", navItem!.ClassList);
    }

    [Fact]
    public async Task Get_RendersPageTitleAndSubtitle()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()));

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients");
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal("Clients", document.QuerySelector("h1.page-title")?.TextContent.Trim());
        Assert.Equal(
            "OAuth/OIDC client applications registered with IdentityServer",
            document.QuerySelector("p.page-sub")?.TextContent.Trim());
    }

    // ---------- Step 2: tabular representation ----------

    [Fact]
    public async Task Get_RendersTableColumnHeaders()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()));

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients");
        IDocument document = await GetDocumentAsync(response);

        var headers = document.QuerySelectorAll("ck-responsive-col-head").Select(h => h.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Name", "Client ID", "Type", "Status", "Actions" }, headers);
    }

    [Fact]
    public async Task Get_ClientsExist_RendersOneRowPerClientWithMappedData()
    {
        var result = new ListResult<ClientListItem>
        {
            Items = new[] { MakeItem("alpha"), MakeItem("beta") },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 10
        };
        HttpClient client = CreateClient(MockService(result));

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients");
        IDocument document = await GetDocumentAsync(response);

        IHtmlCollection<IElement> rows = document.QuerySelectorAll("ck-responsive-row");
        Assert.Equal(2, rows.Length);

        string firstRowText = rows[0].TextContent;
        Assert.Contains("Client alpha", firstRowText);
        Assert.Contains("client-alpha", firstRowText);
        Assert.Contains("Authorization Code", firstRowText);
    }

    [Fact]
    public async Task Get_ClientEnabled_RendersEnabledStatusBadge()
    {
        var result = new ListResult<ClientListItem>
        {
            Items = new[] { MakeItem("on") },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10
        };
        HttpClient client = CreateClient(MockService(result));

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients");
        IDocument document = await GetDocumentAsync(response);

        IElement? badge = document.QuerySelector(".status-badge");
        Assert.NotNull(badge);
        Assert.Contains("Enabled", badge!.TextContent);
        Assert.Contains("enabled", badge.ClassList);
    }

    [Fact]
    public async Task Get_ClientDisabled_RendersDisabledStatusBadge()
    {
        var result = new ListResult<ClientListItem>
        {
            Items = new[] { MakeItem("off", false) },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10
        };
        HttpClient client = CreateClient(MockService(result));

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients");
        IDocument document = await GetDocumentAsync(response);

        IElement? badge = document.QuerySelector(".status-badge");
        Assert.NotNull(badge);
        Assert.Contains("Disabled", badge!.TextContent);
        Assert.Contains("disabled", badge.ClassList);
    }

    // ---------- Step 3: filter, pagination, empty state ----------

    [Fact]
    public async Task Get_NoClientsExist_RendersEmptyStateMessage()
    {
        HttpClient client = CreateClient(MockService(EmptyResult()));

        HttpResponseMessage response = await client.GetAsync("/Admin/Clients");
        IDocument document = await GetDocumentAsync(response);

        IElement? emptyState = document.QuerySelector(".empty-state");
        Assert.NotNull(emptyState);
        Assert.Empty(document.QuerySelectorAll("ck-responsive-row"));
    }
}