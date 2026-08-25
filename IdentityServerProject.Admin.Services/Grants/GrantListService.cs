using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.AuditLogs;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.Grants;

public class GrantListService : IGrantListService
{
    private readonly PersistedGrantDbContext _persistedGrantDbContext;
    private readonly ConfigurationDbContext _configurationDbContext;
    private readonly IAuditWriter _auditWriter;

    public GrantListService(
        PersistedGrantDbContext persistedGrantDbContext,
        ConfigurationDbContext configurationDbContext,
        IAuditWriter auditWriter)
    {
        _persistedGrantDbContext = persistedGrantDbContext;
        _configurationDbContext = configurationDbContext;
        _auditWriter = auditWriter;
    }

    #region Listing

    public async Task<ListResult<GrantListItem>> GetGrantsAsync(
        string? subjectId,
        string? clientId,
        string? typeFilter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default)
    {
        pagination = pagination.Normalize();

        var query = _persistedGrantDbContext.PersistedGrants.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            var escapedSubjectId = LikeExtensions.EscapeLikePattern(subjectId.Trim());
            query = query.Where(g => g.SubjectId != null && EF.Functions.Like(g.SubjectId, $"%{escapedSubjectId}%"));
        }

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            var escapedClientId = LikeExtensions.EscapeLikePattern(clientId.Trim());
            query = query.Where(g => EF.Functions.Like(g.ClientId, $"%{escapedClientId}%"));
        }

        if (!string.IsNullOrWhiteSpace(typeFilter))
        {
            var escapedGrantType = LikeExtensions.EscapeLikePattern(typeFilter.Trim());
            query = query.Where(g => EF.Functions.Like(g.Type, $"%{escapedGrantType}%"));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var grantsOnPage = await query
            .OrderByDescending(g => g.CreationTime)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        var clientIdsOnPage = grantsOnPage
            .Select(g => g.ClientId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        var clientNames = new Dictionary<string, string?>();
        if (clientIdsOnPage.Count > 0)
        {
            clientNames = await _configurationDbContext.Clients
                .AsNoTracking()
                .Where(c => clientIdsOnPage.Contains(c.ClientId))
                .ToDictionaryAsync(c => c.ClientId, c => (string?)c.ClientName, cancellationToken);
        }

        var currentUtc = DateTime.UtcNow;

        var items = grantsOnPage.Select(g => new GrantListItem
        {
            Key = g.Key,
            Type = g.Type,
            SubjectId = g.SubjectId,
            SessionId = g.SessionId,
            ClientId = g.ClientId,
            ClientName = (clientNames.TryGetValue(g.ClientId, out var name) && !string.IsNullOrWhiteSpace(name))
                ? name
                : g.ClientId,
            Description = g.Description,
            CreationTime = g.CreationTime,
            Expiration = g.Expiration,
            ExpirationFormatted = FormatRelativeExpiration(g.Expiration, currentUtc),
            IsExpired = g.Expiration.HasValue && g.Expiration.Value <= currentUtc
        }).ToList();

        return new ListResult<GrantListItem>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize,
        };
    }

    #endregion

    #region Revocation

    public Task<RevokeGrantResult> RevokeGrantAsync(string key, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.Revoke,
            key ?? string.Empty,
            key ?? string.Empty,
            () => RevokeGrantCoreAsync(key ?? string.Empty, cancellationToken),
            cancellationToken);

    private async Task<RevokeGrantResult> RevokeGrantCoreAsync(string key, CancellationToken cancellationToken = default)
    {
        var grant = await _persistedGrantDbContext.PersistedGrants
            .FirstOrDefaultAsync(g => g.Key == key, cancellationToken);

        if (grant == null)
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.Grant, AuditActions.Revoke, AuditOutcome.Denied, AuditReasonCodes.NotFound,
                TargetId: key, TargetName: key, Details: "Grant not found."), cancellationToken);
            return RevokeGrantResult.NotFound;
        }

        try
        {
            _persistedGrantDbContext.PersistedGrants.Remove(grant);
            await _persistedGrantDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.Grant, AuditActions.Revoke, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: key, TargetName: grant.SubjectId ?? key,
                Details: $"Revoked grant for client '{grant.ClientId}'"), cancellationToken);

            return RevokeGrantResult.Revoked;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.Revoke, key, key, ex, cancellationToken);
            throw;
        }
    }

    public Task<int> RevokeGrantsBySubjectAsync(string subjectId, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.BulkRevoke,
            subjectId ?? string.Empty,
            subjectId ?? string.Empty,
            () => RevokeGrantsBySubjectCoreAsync(subjectId ?? string.Empty, cancellationToken),
            cancellationToken);

    private async Task<int> RevokeGrantsBySubjectCoreAsync(string subjectId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            // Internal misuse guard, not a real admin action attempt - nothing to audit.
            return 0;
        }

        try
        {
            var grants = await _persistedGrantDbContext.PersistedGrants
                .Where(g => g.SubjectId == subjectId)
                .ToListAsync(cancellationToken);

            var revokedCount = 0;
            if (grants.Count > 0)
            {
                _persistedGrantDbContext.PersistedGrants.RemoveRange(grants);
                revokedCount = await _persistedGrantDbContext.SaveChangesAsync(cancellationToken);
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.Grant, AuditActions.BulkRevoke, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: subjectId, TargetName: subjectId,
                Details: $"Revoked {revokedCount} persisted grant(s) for subject '{subjectId}'"), cancellationToken);

            return revokedCount;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.BulkRevoke, subjectId, subjectId, ex, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Auditing

    private async Task<T> ExecuteAuditedAsync<T>(
        string action,
        string targetId,
        string targetName,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(action, targetId, targetName, ex, cancellationToken);
            throw;
        }
    }

    private async Task AuditFailedAsync(
        string action,
        string targetId,
        string targetName,
        Exception ex,
        CancellationToken cancellationToken)
    {
        const string marker = "IdentityServerProject.Audit.Grant.Failed";
        if (ex.Data.Contains(marker))
        {
            return;
        }

        ex.Data[marker] = true;
        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.Grant, action, AuditOutcome.Failed, AuditReasonCodes.PersistenceFailure,
            TargetId: targetId, TargetName: targetName, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
    }

    #endregion

    #region Formatting

    public static string FormatRelativeExpiration(DateTime? expiration, DateTime utcNow)
    {
        if (!expiration.HasValue)
        {
            return "Never";
        }

        var diff = expiration.Value - utcNow;

        if (diff <= TimeSpan.Zero)
        {
            return "Expired";
        }

        if (diff.TotalDays >= 1)
        {
            var days = (int)Math.Round(diff.TotalDays);
            return $"in {days} day{(days == 1 ? "" : "s")}";
        }

        if (diff.TotalHours >= 1)
        {
            var hours = (int)Math.Round(diff.TotalHours);
            return $"in {hours} hour{(hours == 1 ? "" : "s")}";
        }

        var minutes = Math.Max(1, (int)Math.Round(diff.TotalMinutes));
        return $"in {minutes} minute{(minutes == 1 ? "" : "s")}";
    }

    #endregion
}
