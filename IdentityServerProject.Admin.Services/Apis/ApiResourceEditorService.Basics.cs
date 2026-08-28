using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Scopes;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.Apis;

public partial class ApiResourceEditorService
{
    public async Task<ApiResourceEditorModel?> GetForEditAsync(ScopeName name,
        CancellationToken cancellationToken = default)
    {
        if (name.IsEmpty)
            return null;

        ApiResource? entity = await LoadResourceAsync(name.Value.Trim(), true, cancellationToken);
        return entity == null ? null : MapToEditorModel(entity);
    }

    public Task<SaveApiResourceBasicsResult> SaveBasicsAsync(
        SaveApiResourceBasicsCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            command?.OriginalName == null ? AuditAction.Create : AuditAction.UpdateBasics,
            (command?.Name ?? command?.OriginalName)?.Value ?? string.Empty,
            command?.DisplayName ?? (command?.Name ?? command?.OriginalName)?.Value ?? string.Empty,
            () => SaveBasicsCoreAsync(command!, cancellationToken),
            cancellationToken);

    private async Task<SaveApiResourceBasicsResult> SaveBasicsCoreAsync(
        SaveApiResourceBasicsCommand command,
        CancellationToken cancellationToken = default)
    {
        string? originalName = command.OriginalName?.Value?.Trim();
        string name = command.Name.Value?.Trim() ?? string.Empty;
        string? displayName = NormalizeNullableString(command.DisplayName);
        string? description = NormalizeNullableString(command.Description);
        AuditAction action = originalName == null ? AuditAction.Create : AuditAction.UpdateBasics;

        if (await ValidateBasicsAsync(
                action,
                name,
                displayName,
                description,
                cancellationToken) is { } validationFailure)
            return validationFailure;

        if (await CheckNameCollisionAsync(
                action,
                originalName,
                name,
                displayName,
                cancellationToken) is { } collisionFailure)
            return collisionFailure;

        if (await ApplyBasicsChangesAsync(
                action,
                originalName,
                name,
                displayName,
                description,
                cancellationToken) is { } mutationFailure)
            return mutationFailure;

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
            return await DenyValidationFailureAsync(action, name, displayName, validationErrors,
                cancellationToken);

        return null;
    }

    private async Task<SaveApiResourceBasicsResult?> DenyValidationFailureAsync(
        AuditAction action,
        string name,
        string? displayName,
        ApiResourceBasicsValidationErrors validationErrors,
        CancellationToken cancellationToken)
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

    private async Task<SaveApiResourceBasicsResult?> CheckNameCollisionAsync(
        AuditAction action,
        string? originalName,
        string name,
        string? displayName,
        CancellationToken cancellationToken)
    {
        bool collision = await _configurationDbContext.ApiResources
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
            return HandleResourceCreation(name, displayName, description);

        ApiResource? entity = await _configurationDbContext.ApiResources
            .FirstOrDefaultAsync(r => r.Name == originalName, cancellationToken);

        if (entity == null)
            return await DenyNotFoundAsync(action, originalName, cancellationToken);

        entity.Name = name;
        entity.DisplayName = displayName;
        entity.Description = description;

        return null;
    }

    private SaveApiResourceBasicsResult? HandleResourceCreation(string name, string? displayName, string? description)
    {
        _configurationDbContext.ApiResources.Add(new ApiResource
        {
            Name = name,
            DisplayName = displayName,
            Description = description,
            Enabled = true
        });

        return null;
    }

    private async Task<SaveApiResourceBasicsResult?> DenyNotFoundAsync(
        AuditAction action,
        string originalName,
        CancellationToken cancellationToken)
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
                originalName == null
                    ? $"Created API Resource '{name}'"
                    : $"Updated basic settings for API Resource '{name}'",
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

        return SaveApiResourceBasicsResult.ConflictResult("Basics.Name",
            $"An API resource named '{name}' already exists.");
    }
}