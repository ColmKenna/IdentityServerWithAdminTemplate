using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.Scopes;

public class ScopeUsageService : IScopeUsageService
{
    private readonly ConfigurationDbContext _configurationDbContext;

    public ScopeUsageService(ConfigurationDbContext configurationDbContext)
    {
        _configurationDbContext = configurationDbContext;
    }

    public async Task<Dictionary<string, int>> GetClientReferenceCountsAsync(IEnumerable<string> scopeNames, CancellationToken cancellationToken = default)
    {
        var names = scopeNames.ToList();
        
        var counts = await _configurationDbContext.Clients
            .SelectMany(c => c.AllowedScopes)
            .Where(cs => names.Contains(cs.Scope))
            .GroupBy(cs => cs.Scope)
            .Select(g => new { Scope = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Scope, x => x.Count, cancellationToken);

        return names.ToDictionary(n => n, n => counts.GetValueOrDefault(n, 0));
    }

}
