using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Microsoft.EntityFrameworkCore;

using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.ApiScopes;

public class ApiScopeEditorService : IApiScopeEditorService
{
    private readonly ConfigurationDbContext _configurationDbContext;
    private readonly IAuditWriter _auditWriter;

    public ApiScopeEditorService(ConfigurationDbContext configurationDbContext, IAuditWriter auditWriter)
    {
        _configurationDbContext = configurationDbContext;
        _auditWriter = auditWriter;
    }

    #region Basics

    public async Task<ApiScopeEditorModel?> GetForEditAsync(string name, CancellationToken cancellationToken = default)
    {
        var entity = await LoadScopeAsync(name, asNoTracking: true, cancellationToken);
        return entity == null ? null : MapToEditorModel(entity);
    }

    public Task<ApiScopeCreateResult> CreateAsync(
        string name,
        string? displayName,
        string? description,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.Create,
            name ?? string.Empty,
            displayName ?? name ?? string.Empty,
            () => CreateCoreAsync(name ?? string.Empty, displayName, description, cancellationToken),
            cancellationToken);

    private async Task<ApiScopeCreateResult> CreateCoreAsync(
        string name,
        string? displayName,
        string? description,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            await AuditDeniedAsync(AuditActions.Create, AuditReasonCodes.ValidationFailed, name, displayName ?? name,
                "Scope name is required.", cancellationToken);
            return ApiScopeCreateResult.Failed("Scope name is required.");
        }

        if (!ScopeValidationHelper.IsValidScopeName(name))
        {
            await AuditDeniedAsync(AuditActions.Create, AuditReasonCodes.ValidationFailed, name, displayName ?? name,
                "Scope name contains invalid characters. Spaces are not allowed.", cancellationToken);
            return ApiScopeCreateResult.Failed("Scope name contains invalid characters. Spaces are not allowed.");
        }

        var scopeNameIsInUse = await _configurationDbContext.ApiScopes
            .AsNoTracking()
            .AnyAsync(s => s.Name == name, cancellationToken);

        if (scopeNameIsInUse)
        {
            await AuditDeniedAsync(AuditActions.Create, AuditReasonCodes.NameCollision, name, displayName ?? name,
                "A scope with this name already exists.", cancellationToken);
            return ApiScopeCreateResult.Failed("A scope with this name already exists.");
        }

        var identityResourceNameIsInUse = await _configurationDbContext.IdentityResources
            .AsNoTracking()
            .AnyAsync(r => r.Name == name, cancellationToken);

        if (identityResourceNameIsInUse)
        {
            await AuditDeniedAsync(AuditActions.Create, AuditReasonCodes.NameCollision, name, displayName ?? name,
                "An identity resource with this name already exists.", cancellationToken);
            return ApiScopeCreateResult.Failed("An identity resource with this name already exists.");
        }

        try
        {
            var newScope = new ApiScope
            {
                Name = name,
                DisplayName = displayName,
                Description = description,
                Enabled = true,
            };

            _configurationDbContext.ApiScopes.Add(newScope);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.ApiScope, AuditActions.Create, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: name, TargetName: displayName ?? name,
                Details: $"Created API Scope '{name}'"), cancellationToken);

            return ApiScopeCreateResult.Succeeded();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.Create, name, displayName ?? name, ex, cancellationToken);
            throw;
        }
    }

    public Task<bool> UpdateBasicsAsync(
        string name,
        string? displayName,
        string? description,
        bool enabled,
        bool required,
        bool emphasize,
        bool showInDiscoveryDocument,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.Update,
            name ?? string.Empty,
            displayName ?? name ?? string.Empty,
            () => UpdateBasicsCoreAsync(name ?? string.Empty, displayName, description, enabled, required, emphasize, showInDiscoveryDocument, cancellationToken),
            cancellationToken);

    private async Task<bool> UpdateBasicsCoreAsync(
        string name,
        string? displayName,
        string? description,
        bool enabled,
        bool required,
        bool emphasize,
        bool showInDiscoveryDocument,
        CancellationToken cancellationToken = default)
    {
        var entity = await _configurationDbContext.ApiScopes
            .FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
        if (entity == null)
        {
            await AuditDeniedAsync(AuditActions.Update, AuditReasonCodes.NotFound, name, name,
                $"API Scope '{name}' was not found.", cancellationToken);
            return false;
        }

        try
        {
            entity.DisplayName = displayName;
            entity.Description = description;
            entity.Enabled = enabled;
            entity.Required = required;
            entity.Emphasize = emphasize;
            entity.ShowInDiscoveryDocument = showInDiscoveryDocument;

            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.ApiScope, AuditActions.Update, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: name, TargetName: displayName ?? name,
                Details: $"Updated basic settings for API Scope '{name}'"), cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.Update, name, displayName ?? name, ex, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Claims

    public Task<bool> AddClaimAsync(string name, string claimType, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.AddClaim,
            name ?? string.Empty,
            name ?? string.Empty,
            () => AddClaimCoreAsync(name ?? string.Empty, claimType ?? string.Empty, cancellationToken),
            cancellationToken);

    private async Task<bool> AddClaimCoreAsync(string name, string claimType, CancellationToken cancellationToken = default)
    {
        var entity = await LoadScopeAsync(name, asNoTracking: false, cancellationToken);
        if (entity == null)
        {
            await AuditDeniedAsync(AuditActions.AddClaim, AuditReasonCodes.NotFound, name, name,
                $"API Scope '{name}' was not found.", cancellationToken);
            return false;
        }

        if (entity.UserClaims.All(c => c.Type != claimType))
        {
            try
            {
                entity.UserClaims.Add(new ApiScopeClaim { Type = claimType });
                await _configurationDbContext.SaveChangesAsync(cancellationToken);

                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategories.ApiScope, AuditActions.AddClaim, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                    TargetId: name, TargetName: name,
                    Details: $"Added claim '{claimType}' to API Scope '{name}'"), cancellationToken);
            }
            catch (Exception ex)
            {
                await AuditFailedAsync(AuditActions.AddClaim, name, name, ex, cancellationToken);
                throw;
            }
        }

        return true;
    }

    public Task<bool> RemoveClaimAsync(string name, string claimType, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.RemoveClaim,
            name ?? string.Empty,
            name ?? string.Empty,
            () => RemoveClaimCoreAsync(name ?? string.Empty, claimType ?? string.Empty, cancellationToken),
            cancellationToken);

    private async Task<bool> RemoveClaimCoreAsync(string name, string claimType, CancellationToken cancellationToken = default)
    {
        var entity = await LoadScopeAsync(name, asNoTracking: false, cancellationToken);
        var claim = entity?.UserClaims.FirstOrDefault(c => c.Type == claimType);
        if (entity == null || claim == null)
        {
            await AuditDeniedAsync(AuditActions.RemoveClaim, AuditReasonCodes.NotFound, name, name,
                $"API Scope '{name}' or claim '{claimType}' was not found.", cancellationToken);
            return false;
        }

        try
        {
            entity.UserClaims.Remove(claim);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.ApiScope, AuditActions.RemoveClaim, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: name, TargetName: name,
                Details: $"Removed claim '{claimType}' from API Scope '{name}'"), cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.RemoveClaim, name, name, ex, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Shared infrastructure

    private async Task<ApiScope?> LoadScopeAsync(string name, bool asNoTracking, CancellationToken cancellationToken)
    {
        var query = _configurationDbContext.ApiScopes
            .Include(s => s.UserClaims)
            .AsQueryable();

        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
    }

    private static ApiScopeEditorModel MapToEditorModel(ApiScope entity)
    {
        return new ApiScopeEditorModel
        {
            Name = entity.Name,
            DisplayName = entity.DisplayName,
            Description = entity.Description,
            Enabled = entity.Enabled,
            Required = entity.Required,
            Emphasize = entity.Emphasize,
            ShowInDiscoveryDocument = entity.ShowInDiscoveryDocument,
            Claims = entity.UserClaims.Select(c => c.Type).OrderBy(c => c).ToList(),
        };
    }

    private Task AuditDeniedAsync(string action, string reasonCode, string targetId, string targetName, string details, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.ApiScope, action, AuditOutcome.Denied, reasonCode,
            TargetId: targetId, TargetName: targetName, Details: details), cancellationToken);

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

    private async Task AuditFailedAsync(string action, string targetId, string targetName, Exception ex, CancellationToken cancellationToken)
    {
        const string marker = "IdentityServerProject.Audit.ApiScope.Failed";
        if (ex.Data.Contains(marker))
        {
            return;
        }

        ex.Data[marker] = true;
        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.ApiScope, action, AuditOutcome.Failed, AuditReasonCodes.PersistenceFailure,
            TargetId: targetId, TargetName: targetName, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
    }

    #endregion
}
