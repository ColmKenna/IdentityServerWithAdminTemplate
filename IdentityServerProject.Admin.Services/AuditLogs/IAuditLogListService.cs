using System.Threading;
using System.Threading.Tasks;

namespace IdentityServerProject.Services.AuditLogs;

/// <summary>
/// Retrieves the read-only history of admin actions recorded in the audit trail.
/// </summary>
public interface IAuditLogListService
{
    /// <summary>
    /// Returns a filtered, paged list of audit log entries ordered by timestamp descending.
    /// </summary>
    /// <param name="filter">Optional structured audit filters.</param>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="pageSize">Maximum items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListResult<AuditLogListItem>> GetAuditLogEntriesAsync(
        AuditLogFilter filter,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
}
