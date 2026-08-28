using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Users;

public partial class UserDetailsService
{
    public Task<ClaimChangeResult> AddClaimAsync(UserId userId, UserClaim claim,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.AddClaim,
            userId.Value,
            () => AddClaimCoreAsync(userId, claim.Type ?? string.Empty, claim.Value ?? string.Empty, cancellationToken),
            cancellationToken);

    private async Task<ClaimChangeResult> AddClaimCoreAsync(UserId userId, string claimType, string claimValue,
        CancellationToken cancellationToken)
    {
        string type = ReservedClaimTypePolicy.Normalize(claimType);
        if (type.Length == 0)
            return await DenyClaimTypeRequiredAsync(AuditAction.AddClaim, userId, cancellationToken);

        if (type.Length > ValidationConstants.MaxClaimTypeLength)
            return await DenyClaimTypeTooLongAsync(userId, cancellationToken);

        if (string.IsNullOrWhiteSpace(claimValue))
            return await DenyClaimValueRequiredAsync(userId, cancellationToken);

        if (claimValue.Length > ValidationConstants.MaxClaimValueLength)
            return await DenyClaimValueTooLongAsync(userId, cancellationToken);

        // Reserved-Claim Guard. A stored claim of a framework-owned type is copied onto the
        // signed-in principal verbatim, so allowing free-text types here would let an
        // administrator mint a role grant that no role-based check can see. See
        // ReservedClaimTypePolicy for the full reasoning.
        if (_reservedClaimTypes.IsReserved(type))
            return await DenyReservedClaimTypeAsync(userId, type, cancellationToken);

        ClaimMutationOutcome outcome = await _store.AddClaimAsync(userId,
            new UserClaim(type, claimValue ?? string.Empty), cancellationToken);
        switch (outcome.Status)
        {
            case ClaimMutationStatus.AlreadyExists:
                {
                    const string message = "This exact claim is already assigned to the user.";
                    await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.ValidationFailed, userId,
                        outcome.TargetName,
                        message, cancellationToken);
                    return ClaimChangeResult.Failed(message);
                }

            case ClaimMutationStatus.UserNotFound:
                await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.NotFound, userId, outcome.TargetName,
                    "User not found.", cancellationToken);
                return ClaimChangeResult.Failed("User not found.");

            case ClaimMutationStatus.ValidationFailed:
                await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.ValidationFailed, userId,
                    outcome.TargetName,
                    outcome.ErrorMessage!, cancellationToken);
                return ClaimChangeResult.Failed(outcome.ErrorMessage!);
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.User, AuditAction.AddClaim, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
            userId, outcome.TargetName,
            Details: $"Added claim type '{type}'"), cancellationToken);

        return ClaimChangeResult.Succeeded();
    }

    private async Task<ClaimChangeResult> DenyClaimTypeTooLongAsync(UserId userId, CancellationToken cancellationToken)
    {
        string message = $"Claim type cannot exceed {ValidationConstants.MaxClaimTypeLength} characters.";
        await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.ValidationFailed, userId, userId,
            message, cancellationToken);
        return ClaimChangeResult.Failed(message);
    }

    private async Task<ClaimChangeResult> DenyClaimValueRequiredAsync(UserId userId, CancellationToken cancellationToken)
    {
        const string message = "Claim value is required.";
        await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.ValidationFailed, userId, userId,
            message, cancellationToken);
        return ClaimChangeResult.Failed(message);
    }

    private async Task<ClaimChangeResult> DenyClaimValueTooLongAsync(UserId userId, CancellationToken cancellationToken)
    {
        string message = $"Claim value cannot exceed {ValidationConstants.MaxClaimValueLength} characters.";
        await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.ValidationFailed, userId, userId,
            message, cancellationToken);
        return ClaimChangeResult.Failed(message);
    }

    private async Task<ClaimChangeResult> DenyReservedClaimTypeAsync(UserId userId, string type, CancellationToken cancellationToken)
    {
        string message =
            $"'{type}' is a reserved claim type and cannot be assigned here. Roles are granted on the Roles tab; " +
            "identity and security claims are issued by the framework.";
        await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.ReservedClaimType, userId, userId,
            message, cancellationToken);
        return ClaimChangeResult.Failed(message);
    }

    public Task<ClaimChangeResult> RemoveClaimAsync(UserId userId, UserClaim claim,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.RemoveClaim,
            userId.Value,
            () => RemoveClaimCoreAsync(userId, claim.Type ?? string.Empty, claim.Value ?? string.Empty,
                cancellationToken),
            cancellationToken);

    private async Task<ClaimChangeResult> RemoveClaimCoreAsync(UserId userId, string claimType, string claimValue,
        CancellationToken cancellationToken)
    {
        string type = ReservedClaimTypePolicy.Normalize(claimType);
        if (type.Length == 0)
            return await DenyClaimTypeRequiredAsync(AuditAction.RemoveClaim, userId, cancellationToken);

        // Deliberately not gated by the Reserved-Claim Guard: removing a claim only ever
        // de-escalates, and reserved claims written before this policy existed need a way out.
        ClaimMutationOutcome outcome = await _store.RemoveClaimAsync(userId,
            new UserClaim(type, claimValue ?? string.Empty), cancellationToken);
        switch (outcome.Status)
        {
            case ClaimMutationStatus.UserNotFound:
                await AuditDeniedAsync(AuditAction.RemoveClaim, AuditReasonCode.NotFound, userId, outcome.TargetName,
                    "User not found.", cancellationToken);
                return ClaimChangeResult.Failed("User not found.");

            case ClaimMutationStatus.ValidationFailed:
                await AuditDeniedAsync(AuditAction.RemoveClaim, AuditReasonCode.ValidationFailed, userId,
                    outcome.TargetName,
                    outcome.ErrorMessage!, cancellationToken);
                return ClaimChangeResult.Failed(outcome.ErrorMessage!);
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.User, AuditAction.RemoveClaim, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
            userId, outcome.TargetName,
            Details: $"Removed claim type '{type}'"), cancellationToken);

        return ClaimChangeResult.Succeeded();
    }

    private async Task<ClaimChangeResult> DenyClaimTypeRequiredAsync(AuditAction action, UserId userId, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(action, AuditReasonCode.ValidationFailed, userId, userId,
            "Claim type is required.", cancellationToken);
        return ClaimChangeResult.Failed("Claim type is required.");
    }
}