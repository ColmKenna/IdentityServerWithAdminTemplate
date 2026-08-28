using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IdentityServerProject.Services.Apis;

public partial class ApiResourceEditorService
{
    public Task<AdminMutationResult> AttachScopeAsync(ScopeName name, ScopeName scopeName,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.AttachScope,
            name.Value,
            name.Value,
            () => AttachScopeCoreAsync(name.Value, scopeName, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> AttachScopeCoreAsync(string name, ScopeName scopeName,
        CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        string scopeNameStr = scopeName.Value?.Trim() ?? string.Empty;

        var errors = new ValidationErrorDictionary();
        AddIdentifierError(errors, "Name", "API Resource", name);
        AddIdentifierError(errors, "AttachScope.ScopeName", "Scope", scopeNameStr);
        if (errors.HasErrors)
            return await DenyScopeAttachmentValidationFailureAsync(name, errors, cancellationToken);

        ApiResource? entity = await LoadResourceAsync(name, false, cancellationToken);
        if (entity == null)
            return await DenyResourceNotFoundAsync(AuditAction.AttachScope, name, cancellationToken);

        bool scopeExists = await _configurationDbContext.ApiScopes
            .AsNoTracking()
            .AnyAsync(s => s.Name == scopeNameStr, cancellationToken);
        if (!scopeExists)
            return await DenyAttachScopeNotFoundAsync(name, scopeNameStr, cancellationToken);

        if (entity.Scopes.All(s => s.Scope != scopeNameStr))
            try
            {
                entity.Scopes.Add(new ApiResourceScope { Scope = scopeNameStr });
                await _configurationDbContext.SaveChangesAsync(cancellationToken);

                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategory.ApiResource, AuditAction.AttachScope, AuditOutcome.Succeeded,
                    AuditReasonCode.Succeeded,
                    name, name,
                    Details: $"Attached scope '{scopeNameStr}' to API Resource"), cancellationToken);
            }
            catch (Exception ex)
            {
                await AuditFailedAsync(AuditAction.AttachScope, name, name, ex, cancellationToken);
                throw;
            }

        return AdminMutationResult.Success();
    }

    private async Task<AdminMutationResult> DenyScopeAttachmentValidationFailureAsync(
        string name, ValidationErrorDictionary errors, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.AttachScope, AuditReasonCode.ValidationFailed, name, name,
            "Scope attachment validation failed.", cancellationToken);
        return AdminMutationResult.ValidationFailure(errors);
    }

    private async Task<AdminMutationResult> DenyAttachScopeNotFoundAsync(string name, string scopeNameStr, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.AttachScope, AuditReasonCode.NotFound, name, name,
            $"API Scope '{scopeNameStr}' was not found.", cancellationToken);
        return AdminMutationResult.NotFoundResult();
    }

    public Task<AdminMutationResult> CreateScopeAsync(
        CreateApiResourceScopeCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.CreateScope,
            command?.ResourceName.Value ?? string.Empty,
            command?.ResourceName.Value ?? string.Empty,
            () => CreateScopeCoreAsync(command!, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> CreateScopeCoreAsync(
        CreateApiResourceScopeCommand command,
        CancellationToken cancellationToken = default)
    {
        string name = command.ResourceName.Value?.Trim() ?? string.Empty;
        string scopeName = command.ScopeName?.Trim() ?? string.Empty;
        string? scopeDisplayName = NormalizeNullableString(command.DisplayName);

        var errors = new ValidationErrorDictionary();

        AddIdentifierError(errors, "Name", "API Resource", name);

        if (string.IsNullOrWhiteSpace(scopeName))
            errors.AddError("CreateScope.ScopeName", "Scope name is required.");
        else if (!ScopeValidationHelper.IsValidScopeName(scopeName))
            errors.AddError("CreateScope.ScopeName", "Scope name contains invalid characters.");
        else if (scopeName.Length > ValidationConstants.MaxScopeNameLength)
            errors.AddError("CreateScope.ScopeName",
                $"Scope name cannot exceed {ValidationConstants.MaxScopeNameLength} characters.");

        if (scopeDisplayName != null && scopeDisplayName.Length > ValidationConstants.MaxDisplayNameLength)
            errors.AddError("CreateScope.DisplayName",
                $"Display name cannot exceed {ValidationConstants.MaxDisplayNameLength} characters.");

        if (errors.HasErrors)
            return await DenyScopeCreationValidationFailureAsync(name, errors, cancellationToken);

        ApiResource? entity = await LoadResourceAsync(name, false, cancellationToken);
        if (entity == null)
            return await DenyResourceNotFoundAsync(AuditAction.CreateScope, name, cancellationToken);

        bool scopeCollision = await _configurationDbContext.ApiScopes.AsNoTracking()
            .AnyAsync(s => s.Name == scopeName, cancellationToken);
        bool identityResourceCollision = await _configurationDbContext.IdentityResources.AsNoTracking()
            .AnyAsync(r => r.Name == scopeName, cancellationToken);
        if (scopeCollision || identityResourceCollision)
            return await DenyScopeNameCollisionAsync(name, scopeName, cancellationToken);

        // Perform creation of global ApiScope and attachment to ApiResource in a single atomic transaction
        IExecutionStrategy executionStrategy = _configurationDbContext.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction =
                await _configurationDbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                _configurationDbContext.ApiScopes.Add(new ApiScope
                {
                    Name = scopeName,
                    DisplayName = scopeDisplayName,
                    Enabled = true
                });

                entity.Scopes.Add(new ApiResourceScope { Scope = scopeName });

                await _configurationDbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategory.ApiResource, AuditAction.CreateScope, AuditOutcome.Succeeded,
                    AuditReasonCode.Succeeded,
                    name, name,
                    Details: $"Created and attached new scope '{scopeName}' to API Resource"), cancellationToken);

                return AdminMutationResult.Success();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                await transaction.RollbackAsync(cancellationToken);
                await AuditDeniedAsync(AuditAction.CreateScope, AuditReasonCode.NameCollision, name, name,
                    $"A scope or identity resource named '{scopeName}' already exists.", cancellationToken);
                return AdminMutationResult.ConflictResult("CreateScope.ScopeName",
                    $"A scope or identity resource named '{scopeName}' already exists.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                await AuditFailedAsync(AuditAction.CreateScope, name, name, ex, cancellationToken);
                throw;
            }
        });
    }

    private async Task<AdminMutationResult> DenyScopeCreationValidationFailureAsync(
        string name, ValidationErrorDictionary errors, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.CreateScope, AuditReasonCode.ValidationFailed, name, name,
            "Scope creation validation failed.", cancellationToken);
        return AdminMutationResult.ValidationFailure(errors);
    }

    private async Task<AdminMutationResult> DenyScopeNameCollisionAsync(string name, string scopeName, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.CreateScope, AuditReasonCode.NameCollision, name, name,
            $"A scope or identity resource named '{scopeName}' already exists.", cancellationToken);
        return AdminMutationResult.ConflictResult("CreateScope.ScopeName",
            $"A scope or identity resource named '{scopeName}' already exists.");
    }

    public Task<AdminMutationResult> DetachScopeAsync(ScopeName name, ScopeName scopeName,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.DetachScope,
            name.Value,
            name.Value,
            () => DetachScopeCoreAsync(name.Value, scopeName, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> DetachScopeCoreAsync(string name, ScopeName scopeName,
        CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        string scopeNameStr = scopeName.Value?.Trim() ?? string.Empty;

        ApiResource? entity = await LoadResourceAsync(name, false, cancellationToken);
        ApiResourceScope? scope = entity?.Scopes.FirstOrDefault(s => s.Scope == scopeNameStr);
        if (entity == null || scope == null)
            return await DenyDetachScopeNotFoundAsync(name, scopeNameStr, cancellationToken);

        try
        {
            entity.Scopes.Remove(scope);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiResource, AuditAction.DetachScope, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                name, name,
                Details: $"Detached scope '{scopeNameStr}' from API Resource"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.DetachScope, name, name, ex, cancellationToken);
            throw;
        }
    }

    private async Task<AdminMutationResult> DenyDetachScopeNotFoundAsync(string name, string scopeNameStr, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.DetachScope, AuditReasonCode.NotFound, name, name,
            $"API Resource '{name}' or scope '{scopeNameStr}' was not found.", cancellationToken);
        return AdminMutationResult.NotFoundResult();
    }
}