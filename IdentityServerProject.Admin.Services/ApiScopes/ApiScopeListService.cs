using System.Data;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Scopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IdentityServerProject.Services.ApiScopes;

public class ApiScopeListService : IApiScopeListService
{
    private readonly IAuditWriter _auditWriter;
    private readonly ConfigurationDbContext _configurationDbContext;
    private readonly IScopeUsageService _scopeUsageService;

    public ApiScopeListService(
        ConfigurationDbContext configurationDbContext,
        IScopeUsageService scopeUsageService,
        IAuditWriter auditWriter)
    {
        _configurationDbContext = configurationDbContext;
        _scopeUsageService = scopeUsageService;
        _auditWriter = auditWriter;
    }

    public async Task<ListResult<ApiScopeListItem>> GetApiScopesAsync(
        ListQuery query,
        CancellationToken cancellationToken = default)
    {
        Pagination pagination = query.Pagination.Normalize();

        IQueryable<ApiScope> dbQuery = ApplyFilter(_configurationDbContext.ApiScopes.AsNoTracking(), query.Filter);

        int totalCount = await dbQuery.CountAsync(cancellationToken);

        List<ApiScopeListItem> items = await dbQuery
            .OrderBy(s => s.Name)
            .ThenBy(s => s.DisplayName)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(s => new ApiScopeListItem
            {
                Name = s.Name,
                DisplayName = s.DisplayName,
                Enabled = s.Enabled,
                ClientReferenceCount = 0
            })
            .ToListAsync(cancellationToken);

        var scopeNames = ScopeSet.FromStrings(items.Select(i => i.Name));
        ScopeUsageCounts referenceCounts =
            await _scopeUsageService.GetClientReferenceCountsAsync(scopeNames, cancellationToken);

        foreach (ApiScopeListItem item in items)
            item.ClientReferenceCount = referenceCounts[ScopeName.Create(item.Name)];

        return new ListResult<ApiScopeListItem>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize
        };
    }

    public async Task<ApiScopeDeleteResult> DeleteApiScopeAsync(string name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using IDbContextTransaction transaction =
                await _configurationDbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable,
                    cancellationToken);
            ApiScope? scope = await _configurationDbContext.ApiScopes
                .FirstOrDefaultAsync(s => s.Name == name, cancellationToken);

            if (scope == null)
                return await HandleScopeNotFoundResultAsync(transaction, name, cancellationToken);

            ScopeUsageCounts referenceCounts = await _scopeUsageService.GetClientReferenceCountsAsync(
                ScopeSet.FromStrings(new[] { name }), cancellationToken);
            if (referenceCounts[ScopeName.Create(name)] > 0)
                return await BuildReferencedScopeResultAsync(transaction, name, scope, cancellationToken);

            _configurationDbContext.ApiScopes.Remove(scope);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiScope, AuditAction.Delete, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                name, scope.DisplayName ?? name,
                Details: $"Deleted API Scope '{name}'"), cancellationToken);
            return ApiScopeDeleteResult.Deleted;
        }
        catch (Exception ex)
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiScope, AuditAction.Delete, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
                name, name, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
            throw;
        }
    }

    private async Task<ApiScopeDeleteResult> HandleScopeNotFoundResultAsync(
        IDbContextTransaction transaction, string name, CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.ApiScope, AuditAction.Delete, AuditOutcome.Denied, AuditReasonCode.NotFound,
            name, name, Details: $"API Scope '{name}' was not found."), cancellationToken);
        return ApiScopeDeleteResult.NotFound;
    }

    private async Task<ApiScopeDeleteResult> BuildReferencedScopeResultAsync(
        IDbContextTransaction transaction, string name, ApiScope scope, CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.ApiScope, AuditAction.Delete, AuditOutcome.Denied, AuditReasonCode.ReferencedResource,
            name, scope.DisplayName ?? name,
            Details: $"API Scope '{name}' is referenced by one or more clients."), cancellationToken);
        return ApiScopeDeleteResult.Blocked;
    }

    private static IQueryable<ApiScope> ApplyFilter(IQueryable<ApiScope> query, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return query;

        string? escaped = LikeExtensions.EscapeLikePattern(filter.Trim());
        string pattern = $"%{escaped}%";

        return query.Where(s =>
            EF.Functions.Like(s.Name, pattern) ||
            (s.DisplayName != null && EF.Functions.Like(s.DisplayName, pattern)));
    }
}