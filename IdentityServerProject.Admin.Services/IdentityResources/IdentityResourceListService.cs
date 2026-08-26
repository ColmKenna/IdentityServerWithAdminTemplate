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

namespace IdentityServerProject.Services.IdentityResources;

public class IdentityResourceListService : IIdentityResourceListService
{
    private readonly ConfigurationDbContext _configurationDbContext;
    private readonly IScopeUsageService _scopeUsageService;
    private readonly IAuditWriter _auditWriter;

    public IdentityResourceListService(
        ConfigurationDbContext configurationDbContext,
        IScopeUsageService scopeUsageService,
        IAuditWriter auditWriter)
    {
        _configurationDbContext = configurationDbContext;
        _scopeUsageService = scopeUsageService;
        _auditWriter = auditWriter;
    }

    public async Task<ListResult<IdentityResourceListItem>> GetIdentityResourcesAsync(
        string? filter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default)
    {
        pagination = pagination.Normalize();

        var query = ApplyFilter(_configurationDbContext.IdentityResources.AsNoTracking(), filter);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(r => r.Name)
            .ThenBy(r => r.DisplayName)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(r => new IdentityResourceListItem
            {
                Name = r.Name,
                DisplayName = r.DisplayName,
                Description = r.Description,
                Enabled = r.Enabled,
                Required = r.Required,
                Emphasize = r.Emphasize,
                ShowInDiscoveryDocument = r.ShowInDiscoveryDocument,
                UserClaimsCount = r.UserClaims.Count,
                NonEditable = r.NonEditable,
                ClientReferenceCount = 0,
            })
            .ToListAsync(cancellationToken);

        var scopeNames = ScopeSet.FromStrings(items.Select(i => i.Name));
        var referenceCounts = await _scopeUsageService.GetClientReferenceCountsAsync(scopeNames, cancellationToken);

        foreach (var item in items)
        {
            item.ClientReferenceCount = referenceCounts[ScopeName.Create(item.Name)];
        }

        return new ListResult<IdentityResourceListItem>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize,
        };
    }

    public async Task<IdentityResourceDeleteResult> DeleteIdentityResourceAsync(string name, CancellationToken cancellationToken = default)
    {
        if (BuiltInIdentityResourcePolicy.IsProtectedName(name))
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.IdentityResource, AuditAction.Delete, AuditOutcome.Denied, AuditReasonCode.ProtectedResource,
                TargetId: name, TargetName: name, Details: $"'{name}' is a protected identity resource name."), cancellationToken);
            return IdentityResourceDeleteResult.Blocked;
        }

        try
        {
            await using var transaction = await _configurationDbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var resource = await _configurationDbContext.IdentityResources
                .Include(r => r.UserClaims)
                .FirstOrDefaultAsync(r => r.Name == name, cancellationToken);

            if (resource == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategory.IdentityResource, AuditAction.Delete, AuditOutcome.Denied, AuditReasonCode.NotFound,
                    TargetId: name, TargetName: name, Details: $"Identity Resource '{name}' was not found."), cancellationToken);
                return IdentityResourceDeleteResult.NotFound;
            }

            if (resource.NonEditable)
            {
                await transaction.RollbackAsync(cancellationToken);
                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategory.IdentityResource, AuditAction.Delete, AuditOutcome.Denied, AuditReasonCode.ProtectedResource,
                    TargetId: name, TargetName: resource.DisplayName ?? name, Details: $"Identity Resource '{name}' is not editable."), cancellationToken);
                return IdentityResourceDeleteResult.Blocked;
            }

            var referenceCounts = await _scopeUsageService.GetClientReferenceCountsAsync(
                ScopeSet.FromStrings(new[] { name }), cancellationToken);
            if (referenceCounts[ScopeName.Create(name)] > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategory.IdentityResource, AuditAction.Delete, AuditOutcome.Denied, AuditReasonCode.ReferencedResource,
                    TargetId: name, TargetName: resource.DisplayName ?? name,
                    Details: $"Identity Resource '{name}' is referenced by one or more clients."), cancellationToken);
                return IdentityResourceDeleteResult.Blocked;
            }

            _configurationDbContext.IdentityResources.Remove(resource);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.IdentityResource, AuditAction.Delete, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: name, TargetName: resource.DisplayName ?? name,
                Details: $"Deleted Identity Resource '{name}'"), cancellationToken);
            return IdentityResourceDeleteResult.Deleted;
        }
        catch (Exception ex)
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.IdentityResource, AuditAction.Delete, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
                TargetId: name, TargetName: name, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
            throw;
        }
    }

    private static IQueryable<IdentityResource> ApplyFilter(IQueryable<IdentityResource> query, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return query;
        }

        var escaped = LikeExtensions.EscapeLikePattern(filter.Trim());
        var pattern = $"%{escaped}%";

        return query.Where(r => EF.Functions.Like(r.Name, pattern) ||
                                (r.DisplayName != null && EF.Functions.Like(r.DisplayName, pattern)));
    }
}
