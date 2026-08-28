using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.Apis;

public partial class ApiResourceEditorService
{
    public Task<AdminMutationResult> SetEnabledAsync(ScopeName name, bool enabled,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.SetEnabled,
            name.Value,
            name.Value,
            () => SetEnabledCoreAsync(name.Value, enabled, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> SetEnabledCoreAsync(string name, bool enabled,
        CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        ApiResource? entity =
            await _configurationDbContext.ApiResources.FirstOrDefaultAsync(r => r.Name == name, cancellationToken);
        if (entity == null)
            return await DenyResourceNotFoundAsync(AuditAction.SetEnabled, name, cancellationToken);

        try
        {
            entity.Enabled = enabled;
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiResource, AuditAction.SetEnabled, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                name, name,
                Details: $"Set API Resource enabled status to {enabled}"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.SetEnabled, name, name, ex, cancellationToken);
            throw;
        }
    }

    public Task<AdminMutationResult> DeleteAsync(ScopeName name, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.Delete,
            name.Value,
            name.Value,
            () => DeleteCoreAsync(name.Value, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> DeleteCoreAsync(string name, CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        ApiResource? entity =
            await _configurationDbContext.ApiResources.FirstOrDefaultAsync(r => r.Name == name, cancellationToken);
        if (entity == null)
            return await DenyResourceNotFoundAsync(AuditAction.Delete, name, cancellationToken);

        try
        {
            _configurationDbContext.ApiResources.Remove(entity);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiResource, AuditAction.Delete, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                name, name,
                Details: $"Deleted API Resource '{name}'"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.Delete, name, name, ex, cancellationToken);
            throw;
        }
    }
}