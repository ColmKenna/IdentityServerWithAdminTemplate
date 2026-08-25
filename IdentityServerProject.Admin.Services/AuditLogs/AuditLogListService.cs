using System.Threading;
using System.Threading.Tasks;

namespace IdentityServerProject.Services.AuditLogs;

public sealed class AuditLogListService : IAuditLogListService
{
    private readonly IAdminAuditStore _store;

    public AuditLogListService(IAdminAuditStore store)
    {
        _store = store;
    }

    public Task<ListResult<AuditLogListItem>> GetAuditLogEntriesAsync(
        AuditLogFilter filter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default) =>
        _store.GetEntriesAsync(filter, pagination, cancellationToken);
}
