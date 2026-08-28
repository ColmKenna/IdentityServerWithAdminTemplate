using Duende.IdentityServer.EntityFramework.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.Scopes;

public class ScopeUsageService(ConfigurationDbContext configurationDbContext) : IScopeUsageService
{
    private readonly ConfigurationDbContext _configurationDbContext = configurationDbContext;

    public async Task<ScopeUsageCounts> GetClientReferenceCountsAsync(ScopeSet scopeNames,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> names = scopeNames.ToValues();

        Dictionary<string, int> counts = await _configurationDbContext.Clients
            .SelectMany(c => c.AllowedScopes)
            .Where(cs => names.Contains(cs.Scope))
            .GroupBy(cs => cs.Scope)
            .Select(g => new { Scope = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Scope, x => x.Count, cancellationToken);

        return new ScopeUsageCounts(names.Select(name =>
            new KeyValuePair<ScopeName, int>(ScopeName.Create(name), counts.GetValueOrDefault(name, 0))));
    }
}