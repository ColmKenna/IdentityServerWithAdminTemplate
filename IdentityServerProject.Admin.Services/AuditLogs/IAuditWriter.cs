using System.Threading;
using System.Threading.Tasks;

namespace IdentityServerProject.Services.AuditLogs;

public interface IAuditWriter
{
    Task WriteAsync(AdminAuditEvent auditEvent, CancellationToken cancellationToken = default);
}
