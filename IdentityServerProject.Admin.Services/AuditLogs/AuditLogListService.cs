namespace IdentityServerProject.Services.AuditLogs;

public sealed class AuditLogListService(IAdminAuditStore store) : IAuditLogListService
{
    private readonly IAdminAuditStore _store = store;

    public Task<ListResult<AuditLogListItem>> GetAuditLogEntriesAsync(
        AuditLogFilter filter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default) =>
        _store.GetEntriesAsync(filter, pagination, cancellationToken);
}