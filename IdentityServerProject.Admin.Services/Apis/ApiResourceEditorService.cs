using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityModel;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Secrets;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using static Duende.IdentityServer.Models.HashExtensions;

namespace IdentityServerProject.Services.Apis;

public partial class ApiResourceEditorService : IApiResourceEditorService
{
    private const string SecretType = "SharedSecret";

    private readonly ConfigurationDbContext _configurationDbContext;
    private readonly IAuditWriter _auditWriter;
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

    #region Overview and basics

    public async Task<ApiResourceEditorModel?> GetForEditAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var entity = await LoadResourceAsync(name.Trim(), asNoTracking: true, cancellationToken);
        return entity == null ? null : MapToEditorModel(entity);
    }

    public Task<SaveApiResourceBasicsResult> SaveBasicsAsync(
        SaveApiResourceBasicsCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            command?.OriginalName == null ? AuditAction.Create : AuditAction.UpdateBasics,
            command?.Name ?? command?.OriginalName ?? string.Empty,
            command?.DisplayName ?? command?.Name ?? command?.OriginalName ?? string.Empty,
            () => SaveBasicsCoreAsync(command!, cancellationToken),
            cancellationToken);

    private async Task<SaveApiResourceBasicsResult> SaveBasicsCoreAsync(
        SaveApiResourceBasicsCommand command,
        CancellationToken cancellationToken = default)
    {
        var originalName = command.OriginalName?.Trim();
        var name = command.Name?.Trim() ?? string.Empty;
        var displayName = NormalizeNullableString(command.DisplayName);
        var description = NormalizeNullableString(command.Description);
        var action = originalName == null ? AuditAction.Create : AuditAction.UpdateBasics;

        if (await ValidateBasicsAsync(
                action,
                name,
                displayName,
                description,
                cancellationToken) is { } validationFailure)
        {
            return validationFailure;
        }

        if (await CheckNameCollisionAsync(
                action,
                originalName,
                name,
                displayName,
                cancellationToken) is { } collisionFailure)
        {
            return collisionFailure;
        }

        if (await ApplyBasicsChangesAsync(
                action,
                originalName,
                name,
                displayName,
                description,
                cancellationToken) is { } mutationFailure)
        {
            return mutationFailure;
        }

        return await PersistBasicsAsync(
            action,
            originalName,
            name,
            displayName,
            cancellationToken);
    }

    private async Task<SaveApiResourceBasicsResult?> ValidateBasicsAsync(
        AuditAction action,
        string name,
        string? displayName,
        string? description,
        CancellationToken cancellationToken)
    {
        var validationErrors = ApiResourceBasicsValidationErrors.Validate(name, displayName, description);
        if (validationErrors.HasErrors)
        {
            await AuditDeniedAsync(
                action,
                AuditReasonCode.ValidationFailed,
                name,
                displayName ?? name,
                "API Resource validation failed.",
                cancellationToken);

            return SaveApiResourceBasicsResult.ValidationFailure(validationErrors);
        }

        return null;
    }

    private async Task<SaveApiResourceBasicsResult?> CheckNameCollisionAsync(
        AuditAction action,
        string? originalName,
        string name,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var collision = await _configurationDbContext.ApiResources
            .AsNoTracking()
            .AnyAsync(r => r.Name == name && r.Name != originalName, cancellationToken);

        return collision
            ? await CreateNameCollisionResultAsync(action, name, displayName, cancellationToken)
            : null;
    }

    private async Task<SaveApiResourceBasicsResult?> ApplyBasicsChangesAsync(
        AuditAction action,
        string? originalName,
        string name,
        string? displayName,
        string? description,
        CancellationToken cancellationToken)
    {
        if (originalName == null)
        {
            _configurationDbContext.ApiResources.Add(new ApiResource
            {
                Name = name,
                DisplayName = displayName,
                Description = description,
                Enabled = true,
            });

            return null;
        }

        var entity = await _configurationDbContext.ApiResources
            .FirstOrDefaultAsync(r => r.Name == originalName, cancellationToken);

        if (entity == null)
        {
            await AuditDeniedAsync(
                action,
                AuditReasonCode.NotFound,
                originalName,
                originalName,
                $"API Resource '{originalName}' was not found.",
                cancellationToken);

            return SaveApiResourceBasicsResult.NotFoundResult();
        }

        entity.Name = name;
        entity.DisplayName = displayName;
        entity.Description = description;

        return null;
    }

    private async Task<SaveApiResourceBasicsResult> PersistBasicsAsync(
        AuditAction action,
        string? originalName,
        string name,
        string? displayName,
        CancellationToken cancellationToken)
    {
        try
        {
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await AuditSucceededAsync(
                action,
                name,
                displayName ?? name,
                originalName == null ? $"Created API Resource '{name}'" : $"Updated basic settings for API Resource '{name}'",
                cancellationToken);

            return SaveApiResourceBasicsResult.SucceededResult();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return await CreateNameCollisionResultAsync(action, name, displayName, cancellationToken);
        }
    }

    private async Task<SaveApiResourceBasicsResult> CreateNameCollisionResultAsync(
        AuditAction action,
        string name,
        string? displayName,
        CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(
            action,
            AuditReasonCode.NameCollision,
            name,
            displayName ?? name,
            $"An API Resource named '{name}' already exists.",
            cancellationToken);

        return SaveApiResourceBasicsResult.ConflictResult("Basics.Name", $"An API resource named '{name}' already exists.");
    }

    #endregion

    #region Secrets

    public Task<ApiResourceAddSecretResult> AddSecretAsync(
        CreateSecretCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.GenerateSecret,
            command?.TargetId ?? string.Empty,
            command?.TargetId ?? string.Empty,
            () => AddSecretCoreAsync(command?.TargetId ?? string.Empty, command?.Description, command?.ExpirationUtc, cancellationToken),
            cancellationToken);

    public Task<ApiResourceAddSecretResult> AddSecretAsync(
        AddApiResourceSecretCommand command,
        CancellationToken cancellationToken = default) =>
        AddSecretAsync(command?.ToCreateSecretCommand()!, cancellationToken);

    private async Task<ApiResourceAddSecretResult> AddSecretCoreAsync(
        string name,
        string? rawDescription,
        DateTime? expiration,
        CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        var description = NormalizeNullableString(rawDescription);

        var errors = new ValidationErrorDictionary();

        AddIdentifierError(errors, "Name", "API Resource", name);

        if (description != null && description.Length > ValidationConstants.MaxSecretDescriptionLength)
        {
            errors.AddError("Secret.Description", $"Description cannot exceed {ValidationConstants.MaxSecretDescriptionLength} characters.");
        }

        if (expiration.HasValue && expiration.Value <= _timeProvider.GetUtcNow().UtcDateTime)
        {
            errors.AddError("Secret.Expiration", "Secret expiration date must be in the future.");
        }

        if (errors.HasErrors)
        {
            await AuditDeniedAsync(AuditAction.GenerateSecret, AuditReasonCode.ValidationFailed, name, name,
                "API Resource secret validation failed.", cancellationToken);
            return ApiResourceAddSecretResult.ValidationFailure(errors);
        }

        var entity = await LoadResourceAsync(name, asNoTracking: false, cancellationToken);
        if (entity == null)
        {
            await AuditDeniedAsync(AuditAction.GenerateSecret, AuditReasonCode.NotFound, name, name,
                $"API Resource '{name}' was not found.", cancellationToken);
            return ApiResourceAddSecretResult.NotFound;
        }

        try
        {
            var plaintextSecret = CryptoRandom.CreateUniqueId();

            entity.Secrets.Add(new ApiResourceSecret
            {
                Description = description,
                Value = plaintextSecret.Sha256(),
                Type = SecretType,
                Expiration = expiration,
                Created = _timeProvider.GetUtcNow().UtcDateTime,
            });

            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiResource, AuditAction.GenerateSecret, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: name, TargetName: name,
                Details: "Added secret to API Resource"), cancellationToken);

            return ApiResourceAddSecretResult.Succeeded(plaintextSecret);
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.GenerateSecret, name, name, ex, cancellationToken);
            throw;
        }
    }

    public Task<AdminMutationResult> RevokeSecretAsync(string name, int secretId, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.RevokeSecret,
            name ?? string.Empty,
            name ?? string.Empty,
            () => RevokeSecretCoreAsync(name ?? string.Empty, secretId, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> RevokeSecretCoreAsync(string name, int secretId, CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        var entity = await LoadResourceAsync(name, asNoTracking: false, cancellationToken);
        var secret = entity?.Secrets.FirstOrDefault(s => s.Id == secretId);
        if (entity == null || secret == null)
        {
            await AuditDeniedAsync(AuditAction.RevokeSecret, AuditReasonCode.NotFound, name, name,
                $"API Resource '{name}' or secret {secretId} was not found.", cancellationToken);
            return AdminMutationResult.NotFoundResult();
        }

        try
        {
            entity.Secrets.Remove(secret);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiResource, AuditAction.RevokeSecret, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: name, TargetName: name,
                Details: "Revoked secret from API Resource"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.RevokeSecret, name, name, ex, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Scopes

    public Task<AdminMutationResult> AttachScopeAsync(string name, ScopeName scopeName, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.AttachScope,
            name ?? string.Empty,
            name ?? string.Empty,
            () => AttachScopeCoreAsync(name ?? string.Empty, scopeName, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> AttachScopeCoreAsync(string name, ScopeName scopeName, CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        var scopeNameStr = scopeName.Value?.Trim() ?? string.Empty;

        var errors = new ValidationErrorDictionary();
        AddIdentifierError(errors, "Name", "API Resource", name);
        AddIdentifierError(errors, "AttachScope.ScopeName", "Scope", scopeNameStr);
        if (errors.HasErrors)
        {
            await AuditDeniedAsync(AuditAction.AttachScope, AuditReasonCode.ValidationFailed, name, name,
                "Scope attachment validation failed.", cancellationToken);
            return AdminMutationResult.ValidationFailure(errors);
        }

        var entity = await LoadResourceAsync(name, asNoTracking: false, cancellationToken);
        if (entity == null)
        {
            await AuditDeniedAsync(AuditAction.AttachScope, AuditReasonCode.NotFound, name, name,
                $"API Resource '{name}' was not found.", cancellationToken);
            return AdminMutationResult.NotFoundResult();
        }

        var scopeExists = await _configurationDbContext.ApiScopes
            .AsNoTracking()
            .AnyAsync(s => s.Name == scopeNameStr, cancellationToken);
        if (!scopeExists)
        {
            await AuditDeniedAsync(AuditAction.AttachScope, AuditReasonCode.NotFound, name, name,
                $"API Scope '{scopeNameStr}' was not found.", cancellationToken);
            return AdminMutationResult.NotFoundResult();
        }

        if (entity.Scopes.All(s => s.Scope != scopeNameStr))
        {
            try
            {
                entity.Scopes.Add(new ApiResourceScope { Scope = scopeNameStr });
                await _configurationDbContext.SaveChangesAsync(cancellationToken);

                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategory.ApiResource, AuditAction.AttachScope, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                    TargetId: name, TargetName: name,
                    Details: $"Attached scope '{scopeNameStr}' to API Resource"), cancellationToken);
            }
            catch (Exception ex)
            {
                await AuditFailedAsync(AuditAction.AttachScope, name, name, ex, cancellationToken);
                throw;
            }
        }

        return AdminMutationResult.Success();
    }

    public Task<AdminMutationResult> CreateScopeAsync(
        CreateApiResourceScopeCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.CreateScope,
            command?.ResourceName ?? string.Empty,
            command?.ResourceName ?? string.Empty,
            () => CreateScopeCoreAsync(command!, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> CreateScopeCoreAsync(
        CreateApiResourceScopeCommand command,
        CancellationToken cancellationToken = default)
    {
        var name = command.ResourceName?.Trim() ?? string.Empty;
        var scopeName = command.ScopeName?.Trim() ?? string.Empty;
        var scopeDisplayName = NormalizeNullableString(command.DisplayName);

        var errors = new ValidationErrorDictionary();

        AddIdentifierError(errors, "Name", "API Resource", name);

        if (string.IsNullOrWhiteSpace(scopeName))
        {
            errors.AddError("CreateScope.ScopeName", "Scope name is required.");
        }
        else if (!ScopeValidationHelper.IsValidScopeName(scopeName))
        {
            errors.AddError("CreateScope.ScopeName", "Scope name contains invalid characters.");
        }
        else if (scopeName.Length > ValidationConstants.MaxScopeNameLength)
        {
            errors.AddError("CreateScope.ScopeName", $"Scope name cannot exceed {ValidationConstants.MaxScopeNameLength} characters.");
        }

        if (scopeDisplayName != null && scopeDisplayName.Length > ValidationConstants.MaxDisplayNameLength)
        {
            errors.AddError("CreateScope.DisplayName", $"Display name cannot exceed {ValidationConstants.MaxDisplayNameLength} characters.");
        }

        if (errors.HasErrors)
        {
            await AuditDeniedAsync(AuditAction.CreateScope, AuditReasonCode.ValidationFailed, name, name,
                "Scope creation validation failed.", cancellationToken);
            return AdminMutationResult.ValidationFailure(errors);
        }

        var entity = await LoadResourceAsync(name, asNoTracking: false, cancellationToken);
        if (entity == null)
        {
            await AuditDeniedAsync(AuditAction.CreateScope, AuditReasonCode.NotFound, name, name,
                $"API Resource '{name}' was not found.", cancellationToken);
            return AdminMutationResult.NotFoundResult();
        }

        var scopeCollision = await _configurationDbContext.ApiScopes.AsNoTracking().AnyAsync(s => s.Name == scopeName, cancellationToken);
        var identityResourceCollision = await _configurationDbContext.IdentityResources.AsNoTracking().AnyAsync(r => r.Name == scopeName, cancellationToken);
        if (scopeCollision || identityResourceCollision)
        {
            await AuditDeniedAsync(AuditAction.CreateScope, AuditReasonCode.NameCollision, name, name,
                $"A scope or identity resource named '{scopeName}' already exists.", cancellationToken);
            return AdminMutationResult.ConflictResult("CreateScope.ScopeName", $"A scope or identity resource named '{scopeName}' already exists.");
        }

        // Perform creation of global ApiScope and attachment to ApiResource in a single atomic transaction
        var executionStrategy = _configurationDbContext.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _configurationDbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                _configurationDbContext.ApiScopes.Add(new ApiScope
                {
                    Name = scopeName,
                    DisplayName = scopeDisplayName,
                    Enabled = true,
                });

                entity.Scopes.Add(new ApiResourceScope { Scope = scopeName });

                await _configurationDbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategory.ApiResource, AuditAction.CreateScope, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                    TargetId: name, TargetName: name,
                    Details: $"Created and attached new scope '{scopeName}' to API Resource"), cancellationToken);

                return AdminMutationResult.Success();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                await transaction.RollbackAsync(cancellationToken);
                await AuditDeniedAsync(AuditAction.CreateScope, AuditReasonCode.NameCollision, name, name,
                    $"A scope or identity resource named '{scopeName}' already exists.", cancellationToken);
                return AdminMutationResult.ConflictResult("CreateScope.ScopeName", $"A scope or identity resource named '{scopeName}' already exists.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                await AuditFailedAsync(AuditAction.CreateScope, name, name, ex, cancellationToken);
                throw;
            }
        });
    }

    public Task<AdminMutationResult> DetachScopeAsync(string name, ScopeName scopeName, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.DetachScope,
            name ?? string.Empty,
            name ?? string.Empty,
            () => DetachScopeCoreAsync(name ?? string.Empty, scopeName, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> DetachScopeCoreAsync(string name, ScopeName scopeName, CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        var scopeNameStr = scopeName.Value?.Trim() ?? string.Empty;

        var entity = await LoadResourceAsync(name, asNoTracking: false, cancellationToken);
        var scope = entity?.Scopes.FirstOrDefault(s => s.Scope == scopeNameStr);
        if (entity == null || scope == null)
        {
            await AuditDeniedAsync(AuditAction.DetachScope, AuditReasonCode.NotFound, name, name,
                $"API Resource '{name}' or scope '{scopeNameStr}' was not found.", cancellationToken);
            return AdminMutationResult.NotFoundResult();
        }

        try
        {
            entity.Scopes.Remove(scope);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiResource, AuditAction.DetachScope, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: name, TargetName: name,
                Details: $"Detached scope '{scopeNameStr}' from API Resource"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.DetachScope, name, name, ex, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Claims

    public Task<AdminMutationResult> AddClaimAsync(AddApiResourceClaimCommand command, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.AddClaim,
            command?.ResourceName ?? string.Empty,
            command?.ResourceName ?? string.Empty,
            () => AddClaimCoreAsync(command!, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> AddClaimCoreAsync(AddApiResourceClaimCommand command, CancellationToken cancellationToken = default)
    {
        var name = command.ResourceName?.Trim() ?? string.Empty;
        var claimType = command.ClaimType?.Trim() ?? string.Empty;

        var errors = new ValidationErrorDictionary();
        AddIdentifierError(errors, "Name", "API Resource", name);
        if (string.IsNullOrWhiteSpace(claimType))
        {
            errors.AddError("Claim.ClaimType", "Claim type is required.");
        }
        else if (claimType.Length > ValidationConstants.MaxClaimTypeLength)
        {
            errors.AddError("Claim.ClaimType", $"Claim type cannot exceed {ValidationConstants.MaxClaimTypeLength} characters.");
        }
        if (errors.HasErrors)
        {
            await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.ValidationFailed, name, name,
                "API Resource claim validation failed.", cancellationToken);
            return AdminMutationResult.ValidationFailure(errors);
        }

        var entity = await LoadResourceAsync(name, asNoTracking: false, cancellationToken);
        if (entity == null)
        {
            await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.NotFound, name, name,
                $"API Resource '{name}' was not found.", cancellationToken);
            return AdminMutationResult.NotFoundResult();
        }

        if (entity.UserClaims.All(c => c.Type != claimType))
        {
            try
            {
                entity.UserClaims.Add(new ApiResourceClaim { Type = claimType });
                await _configurationDbContext.SaveChangesAsync(cancellationToken);

                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategory.ApiResource, AuditAction.AddClaim, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                    TargetId: name, TargetName: name,
                    Details: $"Added claim '{claimType}' to API Resource"), cancellationToken);
            }
            catch (Exception ex)
            {
                await AuditFailedAsync(AuditAction.AddClaim, name, name, ex, cancellationToken);
                throw;
            }
        }

        return AdminMutationResult.Success();
    }

    public Task<AdminMutationResult> RemoveClaimAsync(string name, string claimType, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.RemoveClaim,
            name ?? string.Empty,
            name ?? string.Empty,
            () => RemoveClaimCoreAsync(name ?? string.Empty, claimType ?? string.Empty, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> RemoveClaimCoreAsync(string name, string claimType, CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        claimType = claimType?.Trim() ?? string.Empty;

        var entity = await LoadResourceAsync(name, asNoTracking: false, cancellationToken);
        var claim = entity?.UserClaims.FirstOrDefault(c => c.Type == claimType);
        if (entity == null || claim == null)
        {
            await AuditDeniedAsync(AuditAction.RemoveClaim, AuditReasonCode.NotFound, name, name,
                $"API Resource '{name}' or claim '{claimType}' was not found.", cancellationToken);
            return AdminMutationResult.NotFoundResult();
        }

        try
        {
            entity.UserClaims.Remove(claim);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiResource, AuditAction.RemoveClaim, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: name, TargetName: name,
                Details: $"Removed claim '{claimType}' from API Resource"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.RemoveClaim, name, name, ex, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Lifecycle

    public Task<AdminMutationResult> SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.SetEnabled,
            name ?? string.Empty,
            name ?? string.Empty,
            () => SetEnabledCoreAsync(name ?? string.Empty, enabled, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> SetEnabledCoreAsync(string name, bool enabled, CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        var entity = await _configurationDbContext.ApiResources.FirstOrDefaultAsync(r => r.Name == name, cancellationToken);
        if (entity == null)
        {
            await AuditDeniedAsync(AuditAction.SetEnabled, AuditReasonCode.NotFound, name, name,
                $"API Resource '{name}' was not found.", cancellationToken);
            return AdminMutationResult.NotFoundResult();
        }

        try
        {
            entity.Enabled = enabled;
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiResource, AuditAction.SetEnabled, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: name, TargetName: name,
                Details: $"Set API Resource enabled status to {enabled}"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.SetEnabled, name, name, ex, cancellationToken);
            throw;
        }
    }

    public Task<AdminMutationResult> DeleteAsync(string name, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.Delete,
            name ?? string.Empty,
            name ?? string.Empty,
            () => DeleteCoreAsync(name ?? string.Empty, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> DeleteCoreAsync(string name, CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        var entity = await _configurationDbContext.ApiResources.FirstOrDefaultAsync(r => r.Name == name, cancellationToken);
        if (entity == null)
        {
            await AuditDeniedAsync(AuditAction.Delete, AuditReasonCode.NotFound, name, name,
                $"API Resource '{name}' was not found.", cancellationToken);
            return AdminMutationResult.NotFoundResult();
        }

        try
        {
            _configurationDbContext.ApiResources.Remove(entity);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiResource, AuditAction.Delete, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: name, TargetName: name,
                Details: $"Deleted API Resource '{name}'"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.Delete, name, name, ex, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Shared infrastructure

    public Task<List<string>> GetAllApiScopeNamesAsync(CancellationToken cancellationToken = default) =>
        _configurationDbContext.ApiScopes
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => s.Name)
            .ToListAsync(cancellationToken);

    private async Task<ApiResource?> LoadResourceAsync(string name, bool asNoTracking, CancellationToken cancellationToken)
    {
        var query = _configurationDbContext.ApiResources
            .Include(r => r.Secrets)
            .Include(r => r.Scopes)
            .Include(r => r.UserClaims)
            .AsQueryable();

        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

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
                    Type = s.Type,
                    Expiration = s.Expiration,
                    Created = s.Created,
                })
                .ToList(),
            Scopes = entity.Scopes.Select(s => s.Scope).OrderBy(s => s).ToList(),
            Claims = entity.UserClaims.Select(c => c.Type).OrderBy(c => c).ToList(),
        };
    }

    private Task AuditDeniedAsync(AuditAction action, AuditReasonCode reasonCode, string targetId, string targetName, string details, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.ApiResource, action, AuditOutcome.Denied, reasonCode,
            TargetId: targetId, TargetName: targetName, Details: details), cancellationToken);

    private Task AuditSucceededAsync(AuditAction action, string targetId, string targetName, string details, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.ApiResource, action, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
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
        const string marker = "IdentityServerProject.Audit.ApiResource.Failed";
        if (ex.Data.Contains(marker))
        {
            return;
        }

        ex.Data[marker] = true;
        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.ApiResource, action, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
            TargetId: targetId, TargetName: targetName, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
    }

    #endregion
}
