using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Apis;

public partial class ApiResourceEditorService
{
    public Task<AdminMutationResult> AddClaimAsync(AddApiResourceClaimCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.AddClaim,
            command?.ResourceName.Value ?? string.Empty,
            command?.ResourceName.Value ?? string.Empty,
            () => AddClaimCoreAsync(command!, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> AddClaimCoreAsync(AddApiResourceClaimCommand command,
        CancellationToken cancellationToken = default)
    {
        string name = command.ResourceName.Value?.Trim() ?? string.Empty;
        string claimType = command.ClaimType.Value?.Trim() ?? string.Empty;

        var errors = new ValidationErrorDictionary();
        AddIdentifierError(errors, "Name", "API Resource", name);
        if (string.IsNullOrWhiteSpace(claimType))
            errors.AddError("Claim.ClaimType", "Claim type is required.");
        else if (claimType.Length > ValidationConstants.MaxClaimTypeLength)
            errors.AddError("Claim.ClaimType",
                $"Claim type cannot exceed {ValidationConstants.MaxClaimTypeLength} characters.");
        if (errors.HasErrors)
            return await DenyClaimValidationFailureAsync(name, errors, cancellationToken);

        ApiResource? entity = await LoadResourceAsync(name, false, cancellationToken);
        if (entity == null)
            return await DenyResourceNotFoundAsync(AuditAction.AddClaim, name, cancellationToken);

        if (entity.UserClaims.All(c => c.Type != claimType))
            try
            {
                entity.UserClaims.Add(new ApiResourceClaim { Type = claimType });
                await _configurationDbContext.SaveChangesAsync(cancellationToken);

                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategory.ApiResource, AuditAction.AddClaim, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                    name, name,
                    Details: $"Added claim '{claimType}' to API Resource"), cancellationToken);
            }
            catch (Exception ex)
            {
                await AuditFailedAsync(AuditAction.AddClaim, name, name, ex, cancellationToken);
                throw;
            }

        return AdminMutationResult.Success();
    }

    private async Task<AdminMutationResult> DenyClaimValidationFailureAsync(
        string name, ValidationErrorDictionary errors, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.ValidationFailed, name, name,
            "API Resource claim validation failed.", cancellationToken);
        return AdminMutationResult.ValidationFailure(errors);
    }

    public Task<AdminMutationResult> RemoveClaimAsync(ScopeName name, ClaimType claimType,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.RemoveClaim,
            name.Value,
            name.Value,
            () => RemoveClaimCoreAsync(name.Value, claimType.Value, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> RemoveClaimCoreAsync(string name, string claimType,
        CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        claimType = claimType?.Trim() ?? string.Empty;

        ApiResource? entity = await LoadResourceAsync(name, false, cancellationToken);
        ApiResourceClaim? claim = entity?.UserClaims.FirstOrDefault(c => c.Type == claimType);
        if (entity == null || claim == null)
            return await DenyRemoveClaimNotFoundAsync(name, claimType, cancellationToken);

        try
        {
            entity.UserClaims.Remove(claim);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.ApiResource, AuditAction.RemoveClaim, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                name, name,
                Details: $"Removed claim '{claimType}' from API Resource"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.RemoveClaim, name, name, ex, cancellationToken);
            throw;
        }
    }

    private async Task<AdminMutationResult> DenyRemoveClaimNotFoundAsync(string name, string claimType, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.RemoveClaim, AuditReasonCode.NotFound, name, name,
            $"API Resource '{name}' or claim '{claimType}' was not found.", cancellationToken);
        return AdminMutationResult.NotFoundResult();
    }
}