using System.Data;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Scopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IdentityServerProject.Services.IdentityResources;

public class IdentityResourceListService(
    ConfigurationDbContext configurationDbContext,
    IScopeUsageService scopeUsageService,
    IAuditWriter auditWriter) : IIdentityResourceListService
{
    private readonly IAuditWriter _auditWriter = auditWriter;
    private readonly ConfigurationDbContext _configurationDbContext = configurationDbContext;
    private readonly IScopeUsageService _scopeUsageService = scopeUsageService;

    public async Task<ListResult<IdentityResourceListItem>> GetIdentityResourcesAsync(
        ListQuery query,
        CancellationToken cancellationToken = default)
    {
        Pagination pagination = query.Pagination.Normalize();

        IQueryable<IdentityResource> dbQuery =
            ApplyFilter(_configurationDbContext.IdentityResources.AsNoTracking(), query.Filter);

        int totalCount = await dbQuery.CountAsync(cancellationToken);

        List<IdentityResourceListItem> items = await dbQuery
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
                ClientReferenceCount = 0
            })
            .ToListAsync(cancellationToken);

        var scopeNames = ScopeSet.FromStrings(items.Select(i => i.Name));
        ScopeUsageCounts referenceCounts =
            await _scopeUsageService.GetClientReferenceCountsAsync(scopeNames, cancellationToken);

        foreach (IdentityResourceListItem item in items)
            item.ClientReferenceCount = referenceCounts[ScopeName.Create(item.Name)];

        return new ListResult<IdentityResourceListItem>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize
        };
    }

    public async Task<IdentityResourceDeleteResult> DeleteIdentityResourceAsync(string name,
        CancellationToken cancellationToken = default)
    {
        if (BuiltInIdentityResourcePolicy.IsProtectedName(name))
            return await DenyProtectedNameBlockedAsync(name, cancellationToken);

        string targetName = name;
        IdentityResourceDeleteResult outcome = IdentityResourceDeleteResult.NotFound;
        AuditReasonCode deniedReason = AuditReasonCode.NotFound;
        string? deniedDetails = null;

        try
        {
            IExecutionStrategy strategy = _configurationDbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _configurationDbContext.ChangeTracker.Clear();
                await using IDbContextTransaction transaction =
                    await _configurationDbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable,
                        cancellationToken);
                IdentityResource? resource = await _configurationDbContext.IdentityResources
                    .Include(r => r.UserClaims)
                    .FirstOrDefaultAsync(r => r.Name == name, cancellationToken);

                if (resource is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    outcome = IdentityResourceDeleteResult.NotFound;
                    deniedReason = AuditReasonCode.NotFound;
                    deniedDetails = $"Identity Resource '{name}' was not found.";
                    return;
                }

                targetName = resource.DisplayName ?? name;

                if (resource.NonEditable)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    outcome = IdentityResourceDeleteResult.Blocked;
                    deniedReason = AuditReasonCode.ProtectedResource;
                    deniedDetails = $"Identity Resource '{name}' is not editable.";
                    return;
                }

                ScopeUsageCounts referenceCounts = await _scopeUsageService.GetClientReferenceCountsAsync(
                    ScopeSet.FromStrings(new[] { name }), cancellationToken);
                if (referenceCounts[ScopeName.Create(name)] > 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    outcome = IdentityResourceDeleteResult.Blocked;
                    deniedReason = AuditReasonCode.ReferencedResource;
                    deniedDetails = $"Identity Resource '{name}' is referenced by one or more clients.";
                    return;
                }

                _configurationDbContext.IdentityResources.Remove(resource);
                await _configurationDbContext.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                outcome = IdentityResourceDeleteResult.Deleted;
            });

            if (outcome != IdentityResourceDeleteResult.Deleted)
            {
                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategory.IdentityResource, AuditAction.Delete, AuditOutcome.Denied,
                    deniedReason,
                    name, targetName, Details: deniedDetails), cancellationToken);
                return outcome;
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.IdentityResource, AuditAction.Delete, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                name, targetName,
                Details: $"Deleted Identity Resource '{name}'"), cancellationToken);
            return IdentityResourceDeleteResult.Deleted;
        }
        catch (Exception ex)
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.IdentityResource, AuditAction.Delete, AuditOutcome.Failed,
                AuditReasonCode.PersistenceFailure,
                name, targetName, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
            throw;
        }
    }

    private async Task<IdentityResourceDeleteResult> DenyProtectedNameBlockedAsync(string name, CancellationToken cancellationToken)
    {
        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.IdentityResource, AuditAction.Delete, AuditOutcome.Denied,
            AuditReasonCode.ProtectedResource,
            name, name, Details: $"'{name}' is a protected identity resource name."), cancellationToken);
        return IdentityResourceDeleteResult.Blocked;
    }

    private static IQueryable<IdentityResource> ApplyFilter(IQueryable<IdentityResource> query, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return query;

        string? escaped = LikeExtensions.EscapeLikePattern(filter.Trim());
        string pattern = $"%{escaped}%";

        return query.Where(r => EF.Functions.Like(r.Name, pattern) ||
                                (r.DisplayName != null && EF.Functions.Like(r.DisplayName, pattern)));
    }
}