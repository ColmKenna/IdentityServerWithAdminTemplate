using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Pages.Admin.AuditLogs;
using IdentityServerProject.Services;
using IdentityServerProject.Services.AuditLogs;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.AuditLogs;

public class AuditLogsIndexPageModelTests
{
    private static Pagination DefaultPagination => Pagination.From(1, TestOptions.PageSize);

    [Fact]
    public async Task OnGetAsync_PopulatesAuditLogEntriesFromService()
    {
        var mockService = new Mock<IAuditLogListService>();
        var expectedResult = new ListResult<AuditLogListItem>
        {
            Items = new List<AuditLogListItem>
            {
                new AuditLogListItem
                {
                    Id = 1,
                    Timestamp = DateTime.UtcNow,
                    ActorName = "admin@sales.local",
                    Category = AuditCategory.Client,
                    Action = AuditAction.From("ClientDeleted"),
                    TargetName = "client-123",
                    Outcome = AuditOutcome.Succeeded,
                    IsSuccess = true,
                    Details = "Deleted via Admin UI",
                }
            },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10
        };

        mockService
            .Setup(s => s.GetAuditLogEntriesAsync(It.Is<AuditLogFilter>(filter => filter.ActorSubjectId == null), DefaultPagination, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var model = new IndexModel(mockService.Object, TestOptions.AdminConsole);

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(expectedResult, model.AuditLogEntries);
    }

    [Fact]
    public async Task OnGetAsync_PassesStructuredFiltersAndPageNumberToService()
    {
        var page2 = Pagination.From(2, TestOptions.PageSize);
        var mockService = new Mock<IAuditLogListService>();
        mockService
            .Setup(s => s.GetAuditLogEntriesAsync(It.Is<AuditLogFilter>(filter =>
                filter.ActorSubjectId == "admin-123" &&
                filter.TargetId == "client-123" &&
                filter.Category == AuditCategory.Client &&
                filter.Action == AuditAction.Update &&
                filter.Outcome == AuditOutcome.Succeeded &&
                filter.CorrelationId == "correlation-123"), page2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ListResult<AuditLogListItem>.Empty(2, TestOptions.PageSize));

        var model = new IndexModel(mockService.Object, TestOptions.AdminConsole)
        {
            Filter = new AuditLogFilter
            {
                ActorSubjectId = "admin-123",
                TargetId = "client-123",
                Category = AuditCategory.Client,
                Action = AuditAction.Update,
                Outcome = AuditOutcome.Succeeded,
                CorrelationId = "correlation-123",
            },
            PageNumber = 2,
        };

        await model.OnGetAsync(CancellationToken.None);

        mockService.Verify(s => s.GetAuditLogEntriesAsync(It.Is<AuditLogFilter>(filter =>
            filter.ActorSubjectId == "admin-123" && filter.TargetId == "client-123"), page2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGetAsync_NormalizesNonPositivePageNumberToOne()
    {
        var mockService = new Mock<IAuditLogListService>();
        mockService
            .Setup(s => s.GetAuditLogEntriesAsync(It.IsAny<AuditLogFilter>(), DefaultPagination, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ListResult<AuditLogListItem>.Empty(1, TestOptions.PageSize));

        var model = new IndexModel(mockService.Object, TestOptions.AdminConsole)
        {
            PageNumber = 0,
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(1, model.PageNumber);
        mockService.Verify(s => s.GetAuditLogEntriesAsync(It.IsAny<AuditLogFilter>(), DefaultPagination, It.IsAny<CancellationToken>()), Times.Once);
    }
}
