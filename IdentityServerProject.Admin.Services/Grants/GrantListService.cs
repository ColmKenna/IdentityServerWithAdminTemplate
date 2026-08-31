using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Users;
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
        GrantFilter? filter = null,
        Pagination pagination = default,
        CancellationToken cancellationToken = default)
    {
        pagination = pagination.Normalize();

        var query = _persistedGrantDbContext.PersistedGrants.AsNoTracking();

        var subjectIdStr = filter?.SubjectId?.Value;
        if (!string.IsNullOrWhiteSpace(subjectIdStr))
        {
            var escapedSubjectId = LikeExtensions.EscapeLikePattern(subjectIdStr.Trim());
            query = query.Where(g => g.SubjectId != null && EF.Functions.Like(g.SubjectId, $"%{escapedSubjectId}%"));
        }

        var clientIdStr = filter?.ClientId?.Value;
        if (!string.IsNullOrWhiteSpace(clientIdStr))
        {
            var escapedClientId = LikeExtensions.EscapeLikePattern(clientIdStr.Trim());
            query = query.Where(g => EF.Functions.Like(g.ClientId, $"%{escapedClientId}%"));
        }

        if (!string.IsNullOrWhiteSpace(filter?.TypeFilter))
        {
            var escapedGrantType = LikeExtensions.EscapeLikePattern(filter.TypeFilter.Trim());
            query = query.Where(g => EF.Functions.Like(g.Type, $"%{escapedGrantType}%"));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var grantsOnPage = await query
            .OrderByDescending(g => g.CreationTime)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(g => new GrantListRow(
                g.Key,
                g.Type,
                g.SubjectId,
                g.SessionId,
                g.ClientId,
                g.Description,
                g.CreationTime,
                g.Expiration))
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
            Key = GrantKey.Create(g.Key),
            Type = g.Type,
            SubjectId = g.SubjectId != null ? UserId.Create(g.SubjectId) : null,
            SessionId = g.SessionId,
            ClientId = ClientId.Create(g.ClientId),
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

    private sealed record GrantListRow(
        string Key,
        string Type,
        string? SubjectId,
        string? SessionId,
        string ClientId,
        string? Description,
        DateTime CreationTime,
        DateTime? Expiration);

    #region Revocation

    public Task<RevokeGrantResult> RevokeGrantAsync(GrantKey key, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.Revoke,
            key.Value ?? string.Empty,
            key.Value ?? string.Empty,
            () => RevokeGrantCoreAsync(key, cancellationToken),
            cancellationToken);

    private async Task<RevokeGrantResult> RevokeGrantCoreAsync(GrantKey key, CancellationToken cancellationToken = default)
    {
        var keyStr = key.Value ?? string.Empty;
        var grant = await _persistedGrantDbContext.PersistedGrants
            .FirstOrDefaultAsync(g => g.Key == keyStr, cancellationToken);

        if (grant == null)
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.Grant, AuditAction.Revoke, AuditOutcome.Denied, AuditReasonCode.NotFound,
                TargetId: keyStr, TargetName: keyStr, Details: "Grant not found."), cancellationToken);
            return RevokeGrantResult.NotFound;
        }

        try
        {
            _persistedGrantDbContext.PersistedGrants.Remove(grant);
            await _persistedGrantDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.Grant, AuditAction.Revoke, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: keyStr, TargetName: grant.SubjectId ?? keyStr,
                Details: $"Revoked grant for client '{grant.ClientId}'"), cancellationToken);

            return RevokeGrantResult.Revoked;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.Revoke, keyStr, keyStr, ex, cancellationToken);
            throw;
        }
    }

    public Task<int> RevokeGrantsBySubjectAsync(UserId subjectId, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.BulkRevoke,
            subjectId.Value ?? string.Empty,
            subjectId.Value ?? string.Empty,
            () => RevokeGrantsBySubjectCoreAsync(subjectId, cancellationToken),
            cancellationToken);

    private async Task<int> RevokeGrantsBySubjectCoreAsync(UserId subjectId, CancellationToken cancellationToken = default)
    {
        var subjectIdStr = subjectId.Value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(subjectIdStr))
        {
            // Internal misuse guard, not a real admin action attempt - nothing to audit.
            return 0;
        }

        try
        {
            var grants = await _persistedGrantDbContext.PersistedGrants
                .Where(g => g.SubjectId == subjectIdStr)
                .ToListAsync(cancellationToken);

            var revokedCount = 0;
            if (grants.Count > 0)
            {
                _persistedGrantDbContext.PersistedGrants.RemoveRange(grants);
                revokedCount = await _persistedGrantDbContext.SaveChangesAsync(cancellationToken);
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.Grant, AuditAction.BulkRevoke, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: subjectIdStr, TargetName: subjectIdStr,
                Details: $"Revoked {revokedCount} persisted grant(s) for subject '{subjectIdStr}'"), cancellationToken);

            return revokedCount;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.BulkRevoke, subjectIdStr, subjectIdStr, ex, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Auditing

    private async Task<T> ExecuteAuditedAsync<T>(
        AuditAction action,
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
        AuditAction action,
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
            AuditCategory.Grant, action, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
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
