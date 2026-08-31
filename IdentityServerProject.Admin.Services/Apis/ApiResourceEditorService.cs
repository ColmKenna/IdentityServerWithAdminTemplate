using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Secrets;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.Apis;

public partial class ApiResourceEditorService : IApiResourceEditorService
{
    private readonly IAuditWriter _auditWriter;
    private readonly ConfigurationDbContext _configurationDbContext;
    private readonly TimeProvider _timeProvider;

    public ApiResourceEditorService(
        ConfigurationDbContext configurationDbContext,
        IAuditWriter auditWriter,
        TimeProvider? timeProvider = null)
    {
        _configurationDbContext = configurationDbContext;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<List<string>> GetAllApiScopeNamesAsync(CancellationToken cancellationToken = default) =>
        _configurationDbContext.ApiScopes
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => s.Name)
            .ToListAsync(cancellationToken);

    private async Task<ApiResource?> LoadResourceAsync(string name, bool asNoTracking,
        CancellationToken cancellationToken)
    {
        IQueryable<ApiResource> query = _configurationDbContext.ApiResources
            .AsSplitQuery()
            .Include(r => r.Secrets)
            .Include(r => r.Scopes)
            .Include(r => r.UserClaims)
            .AsQueryable();

        if (asNoTracking)
            query = query.AsNoTracking();

        return await query.FirstOrDefaultAsync(r => r.Name == name, cancellationToken);
    }

    private static ApiResourceEditorModel MapToEditorModel(ApiResource entity)
    {
        return new ApiResourceEditorModel
        {
            IsNew = false,
            Name = entity.Name,
            DisplayName = entity.DisplayName,
            Description = entity.Description,
            Enabled = entity.Enabled,
            Secrets = entity.Secrets
                .OrderByDescending(s => s.Created)
                .Select(s => new ApiResourceSecretItem
                {
                    Id = s.Id,
                    Description = s.Description,
                    Type = s.Type.ParseSecretType(),
                    Expiration = s.Expiration,
                    Created = s.Created
                })
                .ToList(),
            Scopes = entity.Scopes.Select(s => s.Scope).OrderBy(s => s).ToList(),
            Claims = entity.UserClaims.Select(c => c.Type).OrderBy(c => c).ToList()
        };
    }

    private Task AuditDeniedAsync(AuditAction action, AuditReasonCode reasonCode, string targetId, string targetName,
        string details, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.ApiResource, action, AuditOutcome.Denied, reasonCode,
            targetId, targetName, Details: details), cancellationToken);

    private Task AuditSucceededAsync(AuditAction action, string targetId, string targetName, string details,
        CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.ApiResource, action, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
            targetId, targetName, Details: details), cancellationToken);

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

    private async Task AuditFailedAsync(AuditAction action, string targetId, string targetName, Exception ex,
        CancellationToken cancellationToken)
    {
        const string marker = "IdentityServerProject.Audit.ApiResource.Failed";
        if (ex.Data.Contains(marker))
            return;

        ex.Data[marker] = true;
        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.ApiResource, action, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
            targetId, targetName, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
    }

    private async Task<AdminMutationResult> DenyResourceNotFoundAsync(AuditAction action, string name, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(action, AuditReasonCode.NotFound, name, name,
            $"API Resource '{name}' was not found.", cancellationToken);
        return AdminMutationResult.NotFoundResult();
    }
}