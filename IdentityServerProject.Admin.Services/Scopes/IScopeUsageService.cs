namespace IdentityServerProject.Services.Scopes;

public interface IScopeUsageService
{
    Task<ScopeUsageCounts> GetClientReferenceCountsAsync(ScopeSet scopeNames, CancellationToken cancellationToken = default);
}
