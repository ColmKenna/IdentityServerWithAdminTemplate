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
    /// <param name="pagination">Pagination settings (page number and page size).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListResult<AuditLogListItem>> GetAuditLogEntriesAsync(
        AuditLogFilter filter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default);
}
