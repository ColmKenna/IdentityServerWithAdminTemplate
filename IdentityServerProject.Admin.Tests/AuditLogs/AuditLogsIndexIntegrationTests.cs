using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.AuditLogs;

public class AuditLogsIndexIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private HttpClient CreateClientWithMockedService(IAuditLogListService auditLogListService)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => { services.AddSingleton(auditLogListService); });
        });
        _disposables.Add(factory);

        return factory.CreateClient();
    }

    private static IAuditLogListService MockService(ListResult<AuditLogListItem> result)
    {
        var mock = new Mock<IAuditLogListService>();
        mock.Setup(s =>
                s.GetAuditLogEntriesAsync(It.IsAny<AuditLogFilter>(), It.IsAny<Pagination>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static AuditLogListItem MakeItem(int id, string actor, string action, string target, string details,
        DateTime timestamp) => new()
    {
        Id = id,
        Timestamp = timestamp,
        ActorName = actor,
        Category = AuditCategory.Create("Test"),
        Action = AuditAction.From(action),
        Outcome = AuditOutcome.Succeeded,
        IsSuccess = true,
        TargetName = target,
        Details = details,
        ActorSubjectId = UserId.Create("actor-subject"),
        TargetId = "target-id",
        ReasonCode = AuditReasonCode.Succeeded,
        CorrelationId = "correlation-id",
        IpAddress = "192.0.2.10",
        OldValuesJson = "{\"Enabled\":false}",
        NewValuesJson = "<script>alert('unsafe')</script>"
    };

    [Fact]
    public async Task GetIndex_Returns200_AndRendersAuditLogTable()
    {
        AuditLogListItem item1 = MakeItem(1, "admin@sales.local", "ClientDeleted", "client-1", "Deleted via Admin UI",
            DateTime.UtcNow.AddHours(-1));
        AuditLogListItem item2 = MakeItem(2, "admin2@sales.local", "UserUnlocked", "user-2", "Unlocked account",
            DateTime.UtcNow.AddHours(-2));

        IAuditLogListService service = MockService(new ListResult<AuditLogListItem>
        {
            Items = new[] { item1, item2 },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 10
        });

        HttpClient client = CreateClientWithMockedService(service);

        HttpResponseMessage response = await client.GetAsync("/Admin/AuditLogs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        IDocument document = await context.OpenAsync(req => req.Content(content));

        Assert.NotNull(document.QuerySelector("h1.page-title"));
        Assert.Equal("Audit Log", document.QuerySelector("h1.page-title")?.TextContent?.Trim());

        IHtmlCollection<IElement> rows = document.QuerySelectorAll("ck-responsive-row");
        Assert.Equal(2, rows.Length);

        Assert.Contains("admin@sales.local", rows[0].TextContent);
        Assert.Contains("ClientDeleted", rows[0].TextContent);
        Assert.Contains("client-1", rows[0].TextContent);
        Assert.Contains("actor-subject", rows[0].TextContent);
        Assert.Contains("correlation-id", rows[0].TextContent);
        Assert.Null(document.QuerySelector(".audit-metadata script"));
        Assert.Contains("<script>", document.QuerySelector(".audit-metadata pre:last-of-type")!.TextContent);

        Assert.Contains("admin2@sales.local", rows[1].TextContent);
        Assert.Contains("UserUnlocked", rows[1].TextContent);
    }

    [Fact]
    public async Task GetIndex_WhenNoItems_RendersEmptyState()
    {
        IAuditLogListService service = MockService(ListResult<AuditLogListItem>.Empty());
        HttpClient client = CreateClientWithMockedService(service);

        HttpResponseMessage response = await client.GetAsync("/Admin/AuditLogs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        IDocument document = await context.OpenAsync(req => req.Content(content));

        IElement? emptyState = document.QuerySelector(".empty-state");
        Assert.NotNull(emptyState);
        Assert.Equal("No audit log entries found.", emptyState?.TextContent?.Trim());
    }

    [Fact]
    public async Task GetIndex_RendersStructuredFilterInputs()
    {
        IAuditLogListService service = MockService(ListResult<AuditLogListItem>.Empty());
        HttpClient client = CreateClientWithMockedService(service);

        HttpResponseMessage response = await client.GetAsync("/Admin/AuditLogs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        IDocument document = await context.OpenAsync(req => req.Content(content));

        Assert.NotNull(document.QuerySelector("ck-responsive-table"));
        Assert.NotNull(document.QuerySelector("input[name='Filter.ActorSubjectId']"));
        Assert.NotNull(document.QuerySelector("input[name='Filter.TargetId']"));
        Assert.NotNull(document.QuerySelector("input[name='Filter.Category']"));
        Assert.NotNull(document.QuerySelector("input[name='Filter.Action']"));
        Assert.NotNull(document.QuerySelector("select[name='Filter.Outcome']"));
        Assert.NotNull(document.QuerySelector("input[name='Filter.CorrelationId']"));
        Assert.NotNull(document.QuerySelector("form[method=get]"));
    }

    [Fact]
    public async Task GetIndex_WithRealService_BindsStructuredFiltersAndPaginatesSeededEntries()
    {
        // Uses the real DI-registered IAuditLogListService against the SQLite in-memory
        // ApplicationDbContext (no mocking) to exercise the actual GET search + EF Core
        // Skip/Take pagination end to end, per the WI-33 scenario-review test ownership.
        var factory = new AdminWebFactory();
        _disposables.Add(factory);

        string tag = $"audit-e2e-{Guid.NewGuid():N}";

        await factory.RunInScopeAsync(async sp =>
        {
            ApplicationDbContext dbContext = sp.GetRequiredService<ApplicationDbContext>();
            for (int i = 1; i <= 3; i++)
                dbContext.AuditLogEntries.Add(new AuditLogEntry
                {
                    ActorName = $"{tag}-actor",
                    ActorSubjectId = $"{tag}-subject",
                    Category = "Test",
                    IsSuccess = true,
                    Action = $"{tag}-action-{i}",
                    TargetName = $"{tag}-target-{i}",
                    Details = $"{tag}-details-{i}",
                    Timestamp = DateTime.UtcNow.AddMinutes(-i)
                });
            dbContext.AuditLogEntries.Add(new AuditLogEntry
            {
                ActorName = "unrelated-actor",
                ActorSubjectId = "unrelated-subject",
                Category = "Test",
                IsSuccess = true,
                Action = "unrelated-action",
                Timestamp = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        });

        HttpClient client = factory.CreateClient();

        HttpResponseMessage page1Response =
            await client.GetAsync(
                $"/Admin/AuditLogs?Filter.ActorSubjectId={tag}-subject&Filter.Category=Test&PageNumber=1");
        Assert.Equal(HttpStatusCode.OK, page1Response.StatusCode);

        string page1Content = await page1Response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        IDocument page1Document = await context.OpenAsync(req => req.Content(page1Content));

        IHtmlCollection<IElement> page1Rows = page1Document.QuerySelectorAll("ck-responsive-row");
        Assert.Equal(3, page1Rows.Length);
        Assert.DoesNotContain(page1Rows, row => row.TextContent.Contains("unrelated-action"));
        Assert.Contains("Showing 3 of 3 entries", page1Document.QuerySelector(".pagination-summary")?.TextContent);

        // Most-recent-first ordering: -1 minute entry (action-1) should render before -3 minute entry (action-3).
        Assert.Contains($"{tag}-action-1", page1Rows[0].TextContent);
        Assert.Contains($"{tag}-action-3", page1Rows[2].TextContent);
    }
}