using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IdentityServerProject.Services.Scopes;

public interface IScopeUsageService
{
    Task<Dictionary<string, int>> GetClientReferenceCountsAsync(IEnumerable<string> scopeNames, CancellationToken cancellationToken = default);
}
