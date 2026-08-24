using System.Threading;
using System.Threading.Tasks;

namespace IdentityServerProject.Services.AuditLogs;

public class AuditLogListService : IAuditLogListService
{
    private readonly IAdminAuditStore _store;

    public AuditLogListService(IAdminAuditStore store)
    {
        _store = store;
    }

    public Task<ListResult<AuditLogListItem>> GetAuditLogEntriesAsync(
        AuditLogFilter filter,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        _store.GetEntriesAsync(filter, pageNumber, pageSize, cancellationToken);
}
