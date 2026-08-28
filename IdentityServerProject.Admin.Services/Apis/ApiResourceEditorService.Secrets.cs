using Duende.IdentityModel;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Secrets;
using IdentityServerProject.Services.Validation;
using static Duende.IdentityServer.Models.HashExtensions;

namespace IdentityServerProject.Services.Apis;

public partial class ApiResourceEditorService
{
    public Task<ApiResourceAddSecretResult> AddSecretAsync(
        CreateSecretCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.GenerateSecret,
            command?.TargetId ?? string.Empty,
            command?.TargetId ?? string.Empty,
            () => AddSecretCoreAsync(command?.TargetId ?? string.Empty, command?.Description, command?.ExpirationUtc,
                cancellationToken),
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
        string? description = NormalizeNullableString(rawDescription);

        var errors = new ValidationErrorDictionary();

        AddIdentifierError(errors, "Name", "API Resource", name);

        if (description is not null && description.Length > ValidationConstants.MaxSecretDescriptionLength)
            errors.AddError("Secret.Description",
                $"Description cannot exceed {ValidationConstants.MaxSecretDescriptionLength} characters.");

        if (expiration.HasValue && expiration.Value <= _timeProvider.GetUtcNow().UtcDateTime)
            errors.AddError("Secret.Expiration", "Secret expiration date must be in the future.");

        if (errors.HasErrors)
        {
            await AuditDeniedAsync(AuditAction.GenerateSecret, AuditReasonCode.ValidationFailed, name, name,
                "API Resource secret validation failed.", cancellationToken);
            return ApiResourceAddSecretResult.ValidationFailure(errors);
        }

        ApiResource? entity = await LoadResourceAsync(name, false, cancellationToken);
        if (entity is null)
        {
            await AuditDeniedAsync(AuditAction.GenerateSecret, AuditReasonCode.NotFound, name, name,
                $"API Resource '{name}' was not found.", cancellationToken);
            return ApiResourceAddSecretResult.NotFound;
        }

        try
        {
            string plaintextSecret = CryptoRandom.CreateUniqueId();

            entity.Secrets.Add(new ApiResourceSecret
            {
                Description = description,
                Value = plaintextSecret.Sha256(),
                Type = SecretType.SharedSecret.ToSecretTypeValue(),
                Expiration = expiration,
                Created = _timeProvider.GetUtcNow().UtcDateTime
            });

            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiResource, AuditAction.GenerateSecret, AuditOutcome.Succeeded,
                AuditReasonCode.Succeeded,
                name, name,
                Details: "Added secret to API Resource"), cancellationToken);

            return ApiResourceAddSecretResult.Succeeded(plaintextSecret);
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.GenerateSecret, name, name, ex, cancellationToken);
            throw;
        }
    }

    public Task<AdminMutationResult> RevokeSecretAsync(ScopeName name, int secretId,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.RevokeSecret,
            name.Value,
            name.Value,
            () => RevokeSecretCoreAsync(name.Value, secretId, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> RevokeSecretCoreAsync(string name, int secretId,
        CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        ApiResource? entity = await LoadResourceAsync(name, false, cancellationToken);
        ApiResourceSecret? secret = entity?.Secrets.FirstOrDefault(s => s.Id == secretId);
        if (entity is null || secret is null)
            return await DenyRevokeSecretNotFoundAsync(name, secretId, cancellationToken);

        try
        {
            entity.Secrets.Remove(secret);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiResource, AuditAction.RevokeSecret, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                name, name,
                Details: "Revoked secret from API Resource"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.RevokeSecret, name, name, ex, cancellationToken);
            throw;
        }
    }

    private async Task<AdminMutationResult> DenyRevokeSecretNotFoundAsync(string name, int secretId, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.RevokeSecret, AuditReasonCode.NotFound, name, name,
            $"API Resource '{name}' or secret {secretId} was not found.", cancellationToken);
        return AdminMutationResult.NotFoundResult();
    }
}