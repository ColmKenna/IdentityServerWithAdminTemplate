using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using IdentityServerProject.Data;
using IdentityServerProject.Services.AuditLogs;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.AuditLogs;

public class AuditLogsIndexIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    private HttpClient CreateClientWithMockedService(IAuditLogListService auditLogListService)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(auditLogListService);
            });
        });
        _disposables.Add(factory);

        return factory.CreateClient();
    }

    private static IAuditLogListService MockService(ListResult<AuditLogListItem> result)
    {
        var mock = new Mock<IAuditLogListService>();
        mock.Setup(s => s.GetAuditLogEntriesAsync(It.IsAny<AuditLogFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static AuditLogListItem MakeItem(int id, string actor, string action, string target, string details, DateTime timestamp) => new()
    {
        Id = id,
        Timestamp = timestamp,
        ActorName = actor,
        Category = "Test",
        Action = action,
        Outcome = AuditOutcome.Succeeded,
        IsSuccess = true,
        TargetName = target,
        Details = details,
        ActorSubjectId = "actor-subject",
        TargetId = "target-id",
        ReasonCode = "Succeeded",
        CorrelationId = "correlation-id",
        IpAddress = "192.0.2.10",
        OldValuesJson = "{\"Enabled\":false}",
        NewValuesJson = "<script>alert('unsafe')</script>",
    };

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    [Fact]
    public async Task GetIndex_Returns200_AndRendersAuditLogTable()
    {
        var item1 = MakeItem(1, "admin@sales.local", "ClientDeleted", "client-1", "Deleted via Admin UI", DateTime.UtcNow.AddHours(-1));
        var item2 = MakeItem(2, "admin2@sales.local", "UserUnlocked", "user-2", "Unlocked account", DateTime.UtcNow.AddHours(-2));

        var service = MockService(new ListResult<AuditLogListItem>
        {
            Items = new[] { item1, item2 },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 10
        });

        var client = CreateClientWithMockedService(service);

        var response = await client.GetAsync("/Admin/AuditLogs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(content));

        Assert.NotNull(document.QuerySelector("h1.page-title"));
        Assert.Equal("Audit Log", document.QuerySelector("h1.page-title")?.TextContent?.Trim());

        var rows = document.QuerySelectorAll("ck-responsive-row");
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
        var service = MockService(ListResult<AuditLogListItem>.Empty(1, 10));
        var client = CreateClientWithMockedService(service);

        var response = await client.GetAsync("/Admin/AuditLogs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(content));

        var emptyState = document.QuerySelector(".empty-state");
        Assert.NotNull(emptyState);
        Assert.Equal("No audit log entries found.", emptyState?.TextContent?.Trim());
    }

    [Fact]
    public async Task GetIndex_RendersStructuredFilterInputs()
    {
        var service = MockService(ListResult<AuditLogListItem>.Empty(1, 10));
        var client = CreateClientWithMockedService(service);

        var response = await client.GetAsync("/Admin/AuditLogs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(content));

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

        var tag = $"audit-e2e-{Guid.NewGuid():N}";

        await factory.RunInScopeAsync(async sp =>
        {
            var dbContext = sp.GetRequiredService<ApplicationDbContext>();
            for (var i = 1; i <= 3; i++)
            {
                dbContext.AuditLogEntries.Add(new AuditLogEntry
                {
                    ActorName = $"{tag}-actor",
                    ActorSubjectId = $"{tag}-subject",
                    Category = "Test",
                    IsSuccess = true,
                    Action = $"{tag}-action-{i}",
                    TargetName = $"{tag}-target-{i}",
                    Details = $"{tag}-details-{i}",
                    Timestamp = DateTime.UtcNow.AddMinutes(-i),
                });
            }
            dbContext.AuditLogEntries.Add(new AuditLogEntry
            {
                ActorName = "unrelated-actor",
                ActorSubjectId = "unrelated-subject",
                Category = "Test",
                IsSuccess = true,
                Action = "unrelated-action",
                Timestamp = DateTime.UtcNow,
            });
            await dbContext.SaveChangesAsync();
        });

        var client = factory.CreateClient();

        var page1Response = await client.GetAsync($"/Admin/AuditLogs?Filter.ActorSubjectId={tag}-subject&Filter.Category=Test&PageNumber=1");
        Assert.Equal(HttpStatusCode.OK, page1Response.StatusCode);

        var page1Content = await page1Response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var page1Document = await context.OpenAsync(req => req.Content(page1Content));

        var page1Rows = page1Document.QuerySelectorAll("ck-responsive-row");
        Assert.Equal(3, page1Rows.Length);
        Assert.DoesNotContain(page1Rows, row => row.TextContent.Contains("unrelated-action"));
        Assert.Contains("Showing 3 of 3 entries", page1Document.QuerySelector(".pagination-summary")?.TextContent);

        // Most-recent-first ordering: -1 minute entry (action-1) should render before -3 minute entry (action-3).
        Assert.Contains($"{tag}-action-1", page1Rows[0].TextContent);
        Assert.Contains($"{tag}-action-3", page1Rows[2].TextContent);
    }
}
