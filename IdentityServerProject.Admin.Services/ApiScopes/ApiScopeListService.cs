using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Microsoft.EntityFrameworkCore;

using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Scopes;

namespace IdentityServerProject.Services.ApiScopes;

public class ApiScopeListService : IApiScopeListService
{
    private readonly ConfigurationDbContext _configurationDbContext;
    private readonly IScopeUsageService _scopeUsageService;
    private readonly IAuditWriter _auditWriter;

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
        string? filter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default)
    {
        pagination = pagination.Normalize();

        var query = ApplyFilter(_configurationDbContext.ApiScopes.AsNoTracking(), filter);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
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

        var scopeNames = items.Select(i => i.Name);
        var referenceCounts = await _scopeUsageService.GetClientReferenceCountsAsync(scopeNames, cancellationToken);

        foreach (var item in items)
        {
            item.ClientReferenceCount = referenceCounts[item.Name];
        }

        return new ListResult<ApiScopeListItem>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize,
        };
    }

    public async Task<ApiScopeDeleteResult> DeleteApiScopeAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await _configurationDbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var scope = await _configurationDbContext.ApiScopes
                .FirstOrDefaultAsync(s => s.Name == name, cancellationToken);

            if (scope == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategories.ApiScope, AuditActions.Delete, AuditOutcome.Denied, AuditReasonCodes.NotFound,
                    TargetId: name, TargetName: name, Details: $"API Scope '{name}' was not found."), cancellationToken);
                return ApiScopeDeleteResult.NotFound;
            }

            var referenceCounts = await _scopeUsageService.GetClientReferenceCountsAsync(new[] { name }, cancellationToken);
            if (referenceCounts.GetValueOrDefault(name) > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategories.ApiScope, AuditActions.Delete, AuditOutcome.Denied, AuditReasonCodes.ReferencedResource,
                    TargetId: name, TargetName: scope.DisplayName ?? name,
                    Details: $"API Scope '{name}' is referenced by one or more clients."), cancellationToken);
                return ApiScopeDeleteResult.Blocked;
            }

            _configurationDbContext.ApiScopes.Remove(scope);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.ApiScope, AuditActions.Delete, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: name, TargetName: scope.DisplayName ?? name,
                Details: $"Deleted API Scope '{name}'"), cancellationToken);
            return ApiScopeDeleteResult.Deleted;
        }
        catch (Exception ex)
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.ApiScope, AuditActions.Delete, AuditOutcome.Failed, AuditReasonCodes.PersistenceFailure,
                TargetId: name, TargetName: name, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
            throw;
        }
    }

    private static IQueryable<ApiScope> ApplyFilter(IQueryable<ApiScope> query, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return query;
        }

        var escaped = LikeExtensions.EscapeLikePattern(filter.Trim());
        var pattern = $"%{escaped}%";

        return query.Where(s =>
            EF.Functions.Like(s.Name, pattern) ||
            (s.DisplayName != null && EF.Functions.Like(s.DisplayName, pattern)));
    }
}
