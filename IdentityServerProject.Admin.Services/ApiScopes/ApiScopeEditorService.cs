using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.ApiScopes;

public class ApiScopeEditorService : IApiScopeEditorService
{
    private readonly IAuditWriter _auditWriter;
    private readonly ConfigurationDbContext _configurationDbContext;

    public ApiScopeEditorService(ConfigurationDbContext configurationDbContext, IAuditWriter auditWriter)
    {
        _configurationDbContext = configurationDbContext;
        _auditWriter = auditWriter;
    }

    public Task<AdminMutationResult> CreateAsync(CreateApiScopeCommand command,
        CancellationToken cancellationToken = default) =>
        CreateAsync(command.Name.Value, command.DisplayName, command.Description, cancellationToken);

    public Task<bool> UpdateBasicsAsync(UpdateApiScopeBasicsCommand command,
        CancellationToken cancellationToken = default) =>
        UpdateBasicsAsync(command.Name.Value, command.DisplayName, command.Description, command.Enabled,
            command.Required,
            command.Emphasize, command.ShowInDiscoveryDocument, cancellationToken);

    public async Task<ApiScopeEditorModel?> GetForEditAsync(ScopeName name,
        CancellationToken cancellationToken = default)
    {
        ApiScope? entity = await LoadScopeAsync(name.Value, true, cancellationToken);
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
            return await DenyMissingNameAsync(name, displayName, cancellationToken);

        if (!ScopeValidationHelper.IsValidScopeName(name))
            return await DenyInvalidNameAsync(name, displayName, cancellationToken);

        bool scopeNameIsInUse = await _configurationDbContext.ApiScopes
            .AsNoTracking()
            .AnyAsync(s => s.Name == name, cancellationToken);

        if (scopeNameIsInUse)
            return await DenyNameCollisionAsync(name, displayName, "A scope with this name already exists.",
                cancellationToken);

        bool identityResourceNameIsInUse = await _configurationDbContext.IdentityResources
            .AsNoTracking()
            .AnyAsync(r => r.Name == name, cancellationToken);

        if (identityResourceNameIsInUse)
            return await DenyNameCollisionAsync(name, displayName,
                "An identity resource with this name already exists.", cancellationToken);

        try
        {
            var newScope = new ApiScope
            {
                Name = name,
                DisplayName = displayName,
                Description = description,
                Enabled = true
            };

            _configurationDbContext.ApiScopes.Add(newScope);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiScope, AuditAction.Create, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                name, displayName ?? name,
                Details: $"Created API Scope '{name}'"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.Create, name, displayName ?? name, ex, cancellationToken);
            throw;
        }
    }

    private async Task<AdminMutationResult> DenyMissingNameAsync(
        string name,
        string? displayName,
        CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.Create, AuditReasonCode.ValidationFailed, name, displayName ?? name,
            "Scope name is required.", cancellationToken);
        return AdminMutationResult.ValidationFailure(string.Empty, "Scope name is required.");
    }

    private async Task<AdminMutationResult> DenyInvalidNameAsync(
        string name,
        string? displayName,
        CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.Create, AuditReasonCode.ValidationFailed, name, displayName ?? name,
            "Scope name contains invalid characters. Spaces are not allowed.", cancellationToken);
        return AdminMutationResult.ValidationFailure(string.Empty,
            "Scope name contains invalid characters. Spaces are not allowed.");
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
            () => UpdateBasicsCoreAsync(name ?? string.Empty, displayName, description, enabled, required, emphasize,
                showInDiscoveryDocument, cancellationToken),
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
        ApiScope? entity = await _configurationDbContext.ApiScopes
            .FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
        if (entity == null)
            return await HandleScopeNotFoundAsync(AuditAction.Update, name, cancellationToken);

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
                name, displayName ?? name,
                Details: $"Updated basic settings for API Scope '{name}'"), cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.Update, name, displayName ?? name, ex, cancellationToken);
            throw;
        }
    }

    public Task<bool> AddClaimAsync(ScopeName name, ClaimType claimType,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.AddClaim,
            name.Value,
            name.Value,
            () => AddClaimCoreAsync(name.Value, claimType, cancellationToken),
            cancellationToken);

    private async Task<bool> AddClaimCoreAsync(string name, ClaimType claimType,
        CancellationToken cancellationToken = default)
    {
        if (!claimType.IsValid)
            return await DenyInvalidClaimTypeAsync(name, cancellationToken);

        ApiScope? entity = await LoadScopeAsync(name, false, cancellationToken);
        if (entity == null)
            return await HandleScopeNotFoundAsync(AuditAction.AddClaim, name, cancellationToken);

        if (entity.UserClaims.All(c => c.Type != claimType.Value))
            try
            {
                entity.UserClaims.Add(new ApiScopeClaim { Type = claimType.Value });
                await _configurationDbContext.SaveChangesAsync(cancellationToken);

                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategory.ApiScope, AuditAction.AddClaim, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                    name, name,
                    Details: $"Added claim '{claimType}' to API Scope '{name}'"), cancellationToken);
            }
            catch (Exception ex)
            {
                await AuditFailedAsync(AuditAction.AddClaim, name, name, ex, cancellationToken);
                throw;
            }

        return true;
    }

    private async Task<bool> DenyInvalidClaimTypeAsync(string name, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.ValidationFailed, name, name,
            "Claim type is required and must not exceed the maximum length.", cancellationToken);
        return false;
    }

    public Task<bool> RemoveClaimAsync(ScopeName name, ClaimType claimType,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.RemoveClaim,
            name.Value,
            name.Value,
            () => RemoveClaimCoreAsync(name.Value, claimType, cancellationToken),
            cancellationToken);

    private async Task<bool> RemoveClaimCoreAsync(string name, ClaimType claimType,
        CancellationToken cancellationToken = default)
    {
        ApiScope? entity = await LoadScopeAsync(name, false, cancellationToken);
        ApiScopeClaim? claim = entity?.UserClaims.FirstOrDefault(c => c.Type == claimType.Value);
        if (entity == null || claim == null)
            return await DenyClaimNotFoundAsync(name, claimType, cancellationToken);

        try
        {
            entity.UserClaims.Remove(claim);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiScope, AuditAction.RemoveClaim, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                name, name,
                Details: $"Removed claim '{claimType}' from API Scope '{name}'"), cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.RemoveClaim, name, name, ex, cancellationToken);
            throw;
        }
    }

    private async Task<bool> DenyClaimNotFoundAsync(string name, ClaimType claimType,
        CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.RemoveClaim, AuditReasonCode.NotFound, name, name,
            $"API Scope '{name}' or claim '{claimType}' was not found.", cancellationToken);
        return false;
    }

    private async Task<ApiScope?> LoadScopeAsync(string name, bool asNoTracking, CancellationToken cancellationToken)
    {
        IQueryable<ApiScope> query = _configurationDbContext.ApiScopes
            .Include(s => s.UserClaims)
            .AsQueryable();

        if (asNoTracking)
            query = query.AsNoTracking();

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
            Claims = entity.UserClaims.Select(c => c.Type).OrderBy(c => c).ToList()
        };
    }

    private Task AuditDeniedAsync(AuditAction action, AuditReasonCode reasonCode, string targetId, string targetName,
        string details, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.ApiScope, action, AuditOutcome.Denied, reasonCode,
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
        const string marker = "IdentityServerProject.Audit.ApiScope.Failed";
        if (ex.Data.Contains(marker))
            return;

        ex.Data[marker] = true;
        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.ApiScope, action, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
            targetId, targetName, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
    }

    private async Task<AdminMutationResult> DenyNameCollisionAsync(
        string name,
        string? displayName,
        string message,
        CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.Create, AuditReasonCode.NameCollision, name, displayName ?? name,
            message, cancellationToken);
        return AdminMutationResult.ConflictResult(string.Empty, message);
    }

    private async Task<bool> HandleScopeNotFoundAsync(AuditAction action, string name,
        CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(action, AuditReasonCode.NotFound, name, name,
            $"API Scope '{name}' was not found.", cancellationToken);
        return false;
    }
}