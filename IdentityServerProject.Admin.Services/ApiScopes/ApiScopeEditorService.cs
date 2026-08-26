using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Microsoft.EntityFrameworkCore;

using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Scopes;
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

    public Task<AdminMutationResult> CreateAsync(CreateApiScopeCommand command, CancellationToken cancellationToken = default) =>
        CreateAsync(command.Name.Value, command.DisplayName, command.Description, cancellationToken);

    public Task<bool> UpdateBasicsAsync(UpdateApiScopeBasicsCommand command, CancellationToken cancellationToken = default) =>
        UpdateBasicsAsync(command.Name.Value, command.DisplayName, command.Description, command.Enabled, command.Required,
            command.Emphasize, command.ShowInDiscoveryDocument, cancellationToken);

    public async Task<ApiScopeEditorModel?> GetForEditAsync(ScopeName name, CancellationToken cancellationToken = default)
    {
        var entity = await LoadScopeAsync(name.Value, asNoTracking: true, cancellationToken);
        return entity == null ? null : MapToEditorModel(entity);
    }

    public Task<AdminMutationResult> CreateAsync(
        string name,
        string? displayName,
        string? description,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.Create,
            name ?? string.Empty,
            displayName ?? name ?? string.Empty,
            () => CreateCoreAsync(name ?? string.Empty, displayName, description, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> CreateCoreAsync(
        string name,
        string? displayName,
        string? description,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            await AuditDeniedAsync(AuditAction.Create, AuditReasonCode.ValidationFailed, name, displayName ?? name,
                "Scope name is required.", cancellationToken);
            return AdminMutationResult.ValidationFailure(string.Empty, "Scope name is required.");
        }

        if (!ScopeValidationHelper.IsValidScopeName(name))
        {
            await AuditDeniedAsync(AuditAction.Create, AuditReasonCode.ValidationFailed, name, displayName ?? name,
                "Scope name contains invalid characters. Spaces are not allowed.", cancellationToken);
            return AdminMutationResult.ValidationFailure(string.Empty, "Scope name contains invalid characters. Spaces are not allowed.");
        }

        var scopeNameIsInUse = await _configurationDbContext.ApiScopes
            .AsNoTracking()
            .AnyAsync(s => s.Name == name, cancellationToken);

        if (scopeNameIsInUse)
        {
            await AuditDeniedAsync(AuditAction.Create, AuditReasonCode.NameCollision, name, displayName ?? name,
                "A scope with this name already exists.", cancellationToken);
            return AdminMutationResult.ConflictResult(string.Empty, "A scope with this name already exists.");
        }

        var identityResourceNameIsInUse = await _configurationDbContext.IdentityResources
            .AsNoTracking()
            .AnyAsync(r => r.Name == name, cancellationToken);

        if (identityResourceNameIsInUse)
        {
            await AuditDeniedAsync(AuditAction.Create, AuditReasonCode.NameCollision, name, displayName ?? name,
                "An identity resource with this name already exists.", cancellationToken);
            return AdminMutationResult.ConflictResult(string.Empty, "An identity resource with this name already exists.");
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
                AuditCategory.ApiScope, AuditAction.Create, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: name, TargetName: displayName ?? name,
                Details: $"Created API Scope '{name}'"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.Create, name, displayName ?? name, ex, cancellationToken);
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
            AuditAction.Update,
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
            await AuditDeniedAsync(AuditAction.Update, AuditReasonCode.NotFound, name, name,
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
                AuditCategory.ApiScope, AuditAction.Update, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: name, TargetName: displayName ?? name,
                Details: $"Updated basic settings for API Scope '{name}'"), cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.Update, name, displayName ?? name, ex, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Claims

    public Task<bool> AddClaimAsync(ScopeName name, ClaimType claimType, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.AddClaim,
            name.Value,
            name.Value,
            () => AddClaimCoreAsync(name.Value, claimType, cancellationToken),
            cancellationToken);

    private async Task<bool> AddClaimCoreAsync(string name, ClaimType claimType, CancellationToken cancellationToken = default)
    {
        if (!claimType.IsValid)
        {
            await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.ValidationFailed, name, name,
                "Claim type is required and must not exceed the maximum length.", cancellationToken);
            return false;
        }

        var entity = await LoadScopeAsync(name, asNoTracking: false, cancellationToken);
        if (entity == null)
        {
            await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.NotFound, name, name,
                $"API Scope '{name}' was not found.", cancellationToken);
            return false;
        }

        if (entity.UserClaims.All(c => c.Type != claimType.Value))
        {
            try
            {
                entity.UserClaims.Add(new ApiScopeClaim { Type = claimType.Value });
                await _configurationDbContext.SaveChangesAsync(cancellationToken);

                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategory.ApiScope, AuditAction.AddClaim, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                    TargetId: name, TargetName: name,
                    Details: $"Added claim '{claimType}' to API Scope '{name}'"), cancellationToken);
            }
            catch (Exception ex)
            {
                await AuditFailedAsync(AuditAction.AddClaim, name, name, ex, cancellationToken);
                throw;
            }
        }

        return true;
    }

    public Task<bool> RemoveClaimAsync(ScopeName name, ClaimType claimType, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.RemoveClaim,
            name.Value,
            name.Value,
            () => RemoveClaimCoreAsync(name.Value, claimType, cancellationToken),
            cancellationToken);

    private async Task<bool> RemoveClaimCoreAsync(string name, ClaimType claimType, CancellationToken cancellationToken = default)
    {
        var entity = await LoadScopeAsync(name, asNoTracking: false, cancellationToken);
        var claim = entity?.UserClaims.FirstOrDefault(c => c.Type == claimType.Value);
        if (entity == null || claim == null)
        {
            await AuditDeniedAsync(AuditAction.RemoveClaim, AuditReasonCode.NotFound, name, name,
                $"API Scope '{name}' or claim '{claimType}' was not found.", cancellationToken);
            return false;
        }

        try
        {
            entity.UserClaims.Remove(claim);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiScope, AuditAction.RemoveClaim, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: name, TargetName: name,
                Details: $"Removed claim '{claimType}' from API Scope '{name}'"), cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.RemoveClaim, name, name, ex, cancellationToken);
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

    private Task AuditDeniedAsync(AuditAction action, AuditReasonCode reasonCode, string targetId, string targetName, string details, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.ApiScope, action, AuditOutcome.Denied, reasonCode,
            TargetId: targetId, TargetName: targetName, Details: details), cancellationToken);

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

    private async Task AuditFailedAsync(AuditAction action, string targetId, string targetName, Exception ex, CancellationToken cancellationToken)
    {
        const string marker = "IdentityServerProject.Audit.ApiScope.Failed";
        if (ex.Data.Contains(marker))
        {
            return;
        }

        ex.Data[marker] = true;
        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.ApiScope, action, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
            TargetId: targetId, TargetName: targetName, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
    }

    #endregion
}
