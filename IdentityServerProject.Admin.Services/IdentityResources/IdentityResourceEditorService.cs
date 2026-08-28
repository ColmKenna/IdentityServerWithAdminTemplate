using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.IdentityResources;

public class IdentityResourceEditorService : IIdentityResourceEditorService
{
    private const string OpenIdClaimType = "openid";
    private readonly IAuditWriter _auditWriter;
    private readonly ConfigurationDbContext _configurationDbContext;

    public IdentityResourceEditorService(ConfigurationDbContext configurationDbContext, IAuditWriter auditWriter)
    {
        _configurationDbContext = configurationDbContext;
        _auditWriter = auditWriter;
    }

    public Task<AdminMutationResult> CreateAsync(CreateIdentityResourceCommand command,
        CancellationToken cancellationToken = default) =>
        CreateAsync(command.Name.Value, command.DisplayName, command.Description, command.Enabled, command.Required,
            command.Emphasize, command.ShowInDiscoveryDocument, command.UserClaims, cancellationToken);

    public Task<IdentityResourceEditResult> UpdateBasicsAsync(UpdateIdentityResourceBasicsCommand command,
        CancellationToken cancellationToken = default) =>
        UpdateBasicsAsync(command.Name.Value, command.DisplayName, command.Description, command.Enabled,
            command.Required,
            command.Emphasize, command.ShowInDiscoveryDocument, cancellationToken);

    public async Task<IdentityResourceEditorModel?> GetForEditAsync(ScopeName name,
        CancellationToken cancellationToken = default)
    {
        IdentityResource? entity = await LoadIdentityResourceAsync(name.Value, true, cancellationToken);
        return entity == null ? null : MapToEditorModel(entity);
    }

    public Task<AdminMutationResult> CreateAsync(
        string name,
        string? displayName,
        string? description,
        bool enabled,
        bool required,
        bool emphasize,
        bool showInDiscoveryDocument,
        List<string> userClaims,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.Create,
            name ?? string.Empty,
            displayName ?? name ?? string.Empty,
            () => CreateCoreAsync(name ?? string.Empty, displayName, description, enabled, required, emphasize,
                showInDiscoveryDocument, userClaims ?? new List<string>(), cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> CreateCoreAsync(
        string name,
        string? displayName,
        string? description,
        bool enabled,
        bool required,
        bool emphasize,
        bool showInDiscoveryDocument,
        List<string> userClaims,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return await DenyMissingNameAsync(name, displayName, cancellationToken);

        if (!ScopeValidationHelper.IsValidScopeName(name))
            return await DenyInvalidNameAsync(name, displayName, cancellationToken);

        if (BuiltInIdentityResourcePolicy.IsProtectedName(name))
            return await DenyProtectedNameAsync(name, displayName, cancellationToken);

        bool resourceNameIsInUse = await _configurationDbContext.IdentityResources
            .AsNoTracking()
            .AnyAsync(r => r.Name == name, cancellationToken);

        if (resourceNameIsInUse)
            return await DenyNameCollisionAsync(name, displayName,
                "An identity resource with this name already exists.", cancellationToken);

        bool scopeNameIsInUse = await _configurationDbContext.ApiScopes
            .AsNoTracking()
            .AnyAsync(s => s.Name == name, cancellationToken);

        if (scopeNameIsInUse)
            return await DenyNameCollisionAsync(name, displayName,
                "An API scope with this name already exists.", cancellationToken);

        try
        {
            var newResource = new IdentityResource
            {
                Name = name,
                DisplayName = displayName,
                Description = description,
                Enabled = enabled,
                Required = required,
                Emphasize = emphasize,
                ShowInDiscoveryDocument = showInDiscoveryDocument,
                UserClaims = userClaims?.Select(c => new IdentityResourceClaim { Type = c }).ToList() ??
                             new List<IdentityResourceClaim>()
            };

            _configurationDbContext.IdentityResources.Add(newResource);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.IdentityResource, AuditAction.Create, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                name, displayName ?? name,
                Details: $"Created Identity Resource '{name}'"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.Create, name, displayName ?? name, ex, cancellationToken);
            throw;
        }
    }

    private async Task<AdminMutationResult> DenyMissingNameAsync(string name, string? displayName, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.Create, AuditReasonCode.ValidationFailed, name, displayName ?? name,
            "Resource name is required.", cancellationToken);
        return AdminMutationResult.ValidationFailure(string.Empty, "Resource name is required.");
    }

    private async Task<AdminMutationResult> DenyInvalidNameAsync(string name, string? displayName, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.Create, AuditReasonCode.ValidationFailed, name, displayName ?? name,
            "Resource name contains invalid characters. Spaces are not allowed.", cancellationToken);
        return AdminMutationResult.ValidationFailure(string.Empty,
            "Resource name contains invalid characters. Spaces are not allowed.");
    }

    private async Task<AdminMutationResult> DenyProtectedNameAsync(string name, string? displayName, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.Create, AuditReasonCode.ProtectedResource, name, displayName ?? name,
            $"'{name.Trim()}' is a protected identity resource name and cannot be created here.",
            cancellationToken);
        return AdminMutationResult.DeniedResult(string.Empty,
            $"'{name.Trim()}' is a protected identity resource name and cannot be created here.");
    }

    private async Task<AdminMutationResult> DenyNameCollisionAsync(string name, string? displayName, string message, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.Create, AuditReasonCode.NameCollision, name, displayName ?? name,
            message, cancellationToken);
        return AdminMutationResult.ConflictResult(string.Empty, message);
    }

    public Task<IdentityResourceEditResult> UpdateBasicsAsync(
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

    private async Task<IdentityResourceEditResult> UpdateBasicsCoreAsync(
        string name,
        string? displayName,
        string? description,
        bool enabled,
        bool required,
        bool emphasize,
        bool showInDiscoveryDocument,
        CancellationToken cancellationToken = default)
    {
        IdentityResource? entity = await _configurationDbContext.IdentityResources
            .FirstOrDefaultAsync(r => r.Name == name, cancellationToken);

        if (entity == null)
            return await DenyUpdateBasicsNotFoundAsync(name, cancellationToken);

        if (BuiltInIdentityResourcePolicy.IsProtected(entity))
            return await DenyUpdateBasicsProtectedAsync(name, displayName, entity, cancellationToken);

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
                AuditCategory.IdentityResource, AuditAction.Update, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                name, displayName ?? name,
                Details: $"Updated basic settings for Identity Resource '{name}'"), cancellationToken);

            return IdentityResourceEditResult.Succeeded;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.Update, name, displayName ?? name, ex, cancellationToken);
            throw;
        }
    }

    private async Task<IdentityResourceEditResult> DenyUpdateBasicsNotFoundAsync(string name, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.Update, AuditReasonCode.NotFound, name, name,
            $"Identity Resource '{name}' was not found.", cancellationToken);
        return IdentityResourceEditResult.NotFound;
    }

    private async Task<IdentityResourceEditResult> DenyUpdateBasicsProtectedAsync(
        string name, string? displayName, IdentityResource entity, CancellationToken cancellationToken)
    {
        string message = BuiltInIdentityResourcePolicy.ProtectedMessage(entity.Name);
        await AuditDeniedAsync(AuditAction.Update, AuditReasonCode.ProtectedResource, name, displayName ?? name,
            message, cancellationToken);
        return IdentityResourceEditResult.Protected(message);
    }

    public Task<IdentityResourceEditResult> AddClaimAsync(ScopeName name, ClaimType claimType,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.AddClaim,
            name.Value,
            name.Value,
            () => AddClaimCoreAsync(name.Value, claimType, cancellationToken),
            cancellationToken);

    private async Task<IdentityResourceEditResult> AddClaimCoreAsync(string name, ClaimType claimType,
        CancellationToken cancellationToken = default)
    {
        // "openid" is a scope, never a user claim. Rejecting it on any resource predates the
        // resource-identity guard below and is kept because it is still correct — the two rules
        // are independent, and this one catches a nonsense edit the other has no opinion on.
        if (string.Equals(claimType.Value, OpenIdClaimType, StringComparison.OrdinalIgnoreCase))
            return await DenyAddClaimOpenIdProtectedAsync(name, cancellationToken);

        IdentityResource? entity = await LoadIdentityResourceAsync(name, false, cancellationToken);
        if (entity == null)
            return await DenyAddClaimNotFoundAsync(name, cancellationToken);

        // Previously absent here while both sibling methods checked it. Without this, claims could
        // be added to a protected resource and then never removed, leaving it in exactly the state
        // the protection exists to prevent and with no recovery path in the UI.
        if (BuiltInIdentityResourcePolicy.IsProtected(entity))
            return await DenyAddClaimProtectedAsync(name, entity, cancellationToken);

        if (entity.UserClaims.All(c => c.Type != claimType.Value))
            try
            {
                entity.UserClaims.Add(new IdentityResourceClaim { Type = claimType.Value });
                await _configurationDbContext.SaveChangesAsync(cancellationToken);

                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategory.IdentityResource, AuditAction.AddClaim, AuditOutcome.Succeeded,
                    AuditReasonCode.Succeeded,
                    name, name,
                    Details: $"Added claim '{claimType}' to Identity Resource"), cancellationToken);
            }
            catch (Exception ex)
            {
                await AuditFailedAsync(AuditAction.AddClaim, name, name, ex, cancellationToken);
                throw;
            }

        return IdentityResourceEditResult.Succeeded;
    }

    private async Task<IdentityResourceEditResult> DenyAddClaimOpenIdProtectedAsync(string name, CancellationToken cancellationToken)
    {
        string message =
            $"The '{OpenIdClaimType}' claim is protected and cannot be added. It is a scope, not a user claim.";
        await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.ProtectedResource, name, name, message,
            cancellationToken);
        return IdentityResourceEditResult.Protected(message);
    }

    private async Task<IdentityResourceEditResult> DenyAddClaimNotFoundAsync(string name, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.NotFound, name, name,
            $"Identity Resource '{name}' was not found.", cancellationToken);
        return IdentityResourceEditResult.NotFound;
    }

    private async Task<IdentityResourceEditResult> DenyAddClaimProtectedAsync(string name, IdentityResource entity, CancellationToken cancellationToken)
    {
        string message = BuiltInIdentityResourcePolicy.ProtectedMessage(entity.Name);
        await AuditDeniedAsync(AuditAction.AddClaim, AuditReasonCode.ProtectedResource, name, name, message,
            cancellationToken);
        return IdentityResourceEditResult.Protected(message);
    }

    public Task<IdentityResourceEditResult> RemoveClaimAsync(ScopeName name, ClaimType claimType,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.RemoveClaim,
            name.Value,
            name.Value,
            () => RemoveClaimCoreAsync(name.Value, claimType, cancellationToken),
            cancellationToken);

    private async Task<IdentityResourceEditResult> RemoveClaimCoreAsync(string name, ClaimType claimType,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(claimType.Value, OpenIdClaimType, StringComparison.OrdinalIgnoreCase))
            return await DenyRemoveClaimOpenIdProtectedAsync(name, cancellationToken);

        IdentityResource? entity = await LoadIdentityResourceAsync(name, false, cancellationToken);
        if (entity == null)
            return await DenyRemoveClaimNotFoundAsync(name, cancellationToken);

        // Checked ahead of the resource-level guard so the operator gets the specific reason
        // ("openid needs sub") rather than the general one, and so the invariant still holds if a
        // deployment ever clears the protection flag on a resource that has one.
        if (BuiltInIdentityResourcePolicy.IsInvariantClaim(entity.Name, claimType))
            return await DenyInvariantClaimAsync(name, entity, claimType, cancellationToken);

        if (BuiltInIdentityResourcePolicy.IsProtected(entity))
            return await DenyRemoveClaimProtectedAsync(name, entity, cancellationToken);

        IdentityResourceClaim? claim = entity.UserClaims.FirstOrDefault(c => c.Type == claimType);
        if (claim == null)
            return await DenyClaimNotFoundAsync(name, claimType, cancellationToken);

        try
        {
            entity.UserClaims.Remove(claim);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.IdentityResource, AuditAction.RemoveClaim, AuditOutcome.Succeeded,
                AuditReasonCode.Succeeded,
                name, name,
                Details: $"Removed claim '{claimType}' from Identity Resource"), cancellationToken);

            return IdentityResourceEditResult.Succeeded;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.RemoveClaim, name, name, ex, cancellationToken);
            throw;
        }
    }

    private async Task<IdentityResourceEditResult> DenyRemoveClaimOpenIdProtectedAsync(string name, CancellationToken cancellationToken)
    {
        string message = $"The '{OpenIdClaimType}' claim is protected and cannot be removed.";
        await AuditDeniedAsync(AuditAction.RemoveClaim, AuditReasonCode.ProtectedResource, name, name, message,
            cancellationToken);
        return IdentityResourceEditResult.Protected(message);
    }

    private async Task<IdentityResourceEditResult> DenyRemoveClaimNotFoundAsync(string name, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.RemoveClaim, AuditReasonCode.NotFound, name, name,
            $"Identity Resource '{name}' was not found.", cancellationToken);
        return IdentityResourceEditResult.NotFound;
    }

    private async Task<IdentityResourceEditResult> DenyInvariantClaimAsync(
        string name, IdentityResource entity, ClaimType claimType, CancellationToken cancellationToken)
    {
        string message = BuiltInIdentityResourcePolicy.InvariantClaimMessage(entity.Name, claimType);
        await AuditDeniedAsync(AuditAction.RemoveClaim, AuditReasonCode.ProtectedResource, name, name, message,
            cancellationToken);
        return IdentityResourceEditResult.Protected(message);
    }

    private async Task<IdentityResourceEditResult> DenyRemoveClaimProtectedAsync(string name, IdentityResource entity, CancellationToken cancellationToken)
    {
        string message = BuiltInIdentityResourcePolicy.ProtectedMessage(entity.Name);
        await AuditDeniedAsync(AuditAction.RemoveClaim, AuditReasonCode.ProtectedResource, name, name, message,
            cancellationToken);
        return IdentityResourceEditResult.Protected(message);
    }

    private async Task<IdentityResourceEditResult> DenyClaimNotFoundAsync(string name, ClaimType claimType, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.RemoveClaim, AuditReasonCode.NotFound, name, name,
            $"Claim '{claimType}' was not found on Identity Resource '{name}'.", cancellationToken);
        return IdentityResourceEditResult.NotFound;
    }

    private async Task<IdentityResource?> LoadIdentityResourceAsync(string name, bool asNoTracking,
        CancellationToken cancellationToken)
    {
        IQueryable<IdentityResource> query = _configurationDbContext.IdentityResources
            .Include(r => r.UserClaims)
            .AsQueryable();

        if (asNoTracking)
            query = query.AsNoTracking();

        return await query.FirstOrDefaultAsync(r => r.Name == name, cancellationToken);
    }

    private static IdentityResourceEditorModel MapToEditorModel(IdentityResource entity)
    {
        return new IdentityResourceEditorModel
        {
            Name = entity.Name,
            DisplayName = entity.DisplayName,
            Description = entity.Description,
            Enabled = entity.Enabled,
            Required = entity.Required,
            Emphasize = entity.Emphasize,
            ShowInDiscoveryDocument = entity.ShowInDiscoveryDocument,
            UserClaims = entity.UserClaims.Select(c => c.Type).OrderBy(c => c).ToList(),
            IsProtected = BuiltInIdentityResourcePolicy.IsProtected(entity)
        };
    }

    private Task AuditDeniedAsync(AuditAction action, AuditReasonCode reasonCode, string targetId, string targetName,
        string details, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.IdentityResource, action, AuditOutcome.Denied, reasonCode,
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
        const string marker = "IdentityServerProject.Audit.IdentityResource.Failed";
        if (ex.Data.Contains(marker))
            return;

        ex.Data[marker] = true;
        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.IdentityResource, action, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
            targetId, targetName, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
    }
}