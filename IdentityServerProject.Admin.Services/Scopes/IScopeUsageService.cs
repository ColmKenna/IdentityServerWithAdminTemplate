using System.Threading;
using System.Threading.Tasks;

namespace IdentityServerProject.Services.Scopes;

public interface IScopeUsageService
{
    Task<ScopeUsageCounts> GetClientReferenceCountsAsync(ScopeSet scopeNames, CancellationToken cancellationToken = default);
}
