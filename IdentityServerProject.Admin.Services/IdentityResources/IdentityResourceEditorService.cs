using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Microsoft.EntityFrameworkCore;

using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.IdentityResources;

public class IdentityResourceEditorService : IIdentityResourceEditorService
{
    private const string OpenIdClaimType = "openid";
    private readonly ConfigurationDbContext _configurationDbContext;
    private readonly IAuditWriter _auditWriter;

    public IdentityResourceEditorService(ConfigurationDbContext configurationDbContext, IAuditWriter auditWriter)
    {
        _configurationDbContext = configurationDbContext;
        _auditWriter = auditWriter;
    }

    #region Basics

    public async Task<IdentityResourceEditorModel?> GetForEditAsync(string name, CancellationToken cancellationToken = default)
    {
        var entity = await LoadIdentityResourceAsync(name, asNoTracking: true, cancellationToken);
        return entity == null ? null : MapToEditorModel(entity);
    }

    public Task<IdentityResourceCreateResult> CreateAsync(
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
            AuditActions.Create,
            name ?? string.Empty,
            displayName ?? name ?? string.Empty,
            () => CreateCoreAsync(name ?? string.Empty, displayName, description, enabled, required, emphasize,
                showInDiscoveryDocument, userClaims ?? new List<string>(), cancellationToken),
            cancellationToken);

    private async Task<IdentityResourceCreateResult> CreateCoreAsync(
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
        {
            await AuditDeniedAsync(AuditActions.Create, AuditReasonCodes.ValidationFailed, name, displayName ?? name,
                "Resource name is required.", cancellationToken);
            return IdentityResourceCreateResult.Failed("Resource name is required.");
        }

        if (!ScopeValidationHelper.IsValidScopeName(name))
        {
            await AuditDeniedAsync(AuditActions.Create, AuditReasonCodes.ValidationFailed, name, displayName ?? name,
                "Resource name contains invalid characters. Spaces are not allowed.", cancellationToken);
            return IdentityResourceCreateResult.Failed("Resource name contains invalid characters. Spaces are not allowed.");
        }

        if (BuiltInIdentityResourcePolicy.IsProtectedName(name))
        {
            await AuditDeniedAsync(AuditActions.Create, AuditReasonCodes.ProtectedResource, name, displayName ?? name,
                $"'{name.Trim()}' is a protected identity resource name and cannot be created here.", cancellationToken);
            return IdentityResourceCreateResult.Failed($"'{name.Trim()}' is a protected identity resource name and cannot be created here.");
        }

        var resourceNameIsInUse = await _configurationDbContext.IdentityResources
            .AsNoTracking()
            .AnyAsync(r => r.Name == name, cancellationToken);

        if (resourceNameIsInUse)
        {
            await AuditDeniedAsync(AuditActions.Create, AuditReasonCodes.NameCollision, name, displayName ?? name,
                "An identity resource with this name already exists.", cancellationToken);
            return IdentityResourceCreateResult.Failed("An identity resource with this name already exists.");
        }

        var scopeNameIsInUse = await _configurationDbContext.ApiScopes
            .AsNoTracking()
            .AnyAsync(s => s.Name == name, cancellationToken);

        if (scopeNameIsInUse)
        {
            await AuditDeniedAsync(AuditActions.Create, AuditReasonCodes.NameCollision, name, displayName ?? name,
                "An API scope with this name already exists.", cancellationToken);
            return IdentityResourceCreateResult.Failed("An API scope with this name already exists.");
        }

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
                UserClaims = userClaims?.Select(c => new IdentityResourceClaim { Type = c }).ToList() ?? new()
            };

            _configurationDbContext.IdentityResources.Add(newResource);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.IdentityResource, AuditActions.Create, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: name, TargetName: displayName ?? name,
                Details: $"Created Identity Resource '{name}'"), cancellationToken);

            return IdentityResourceCreateResult.Succeeded();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.Create, name, displayName ?? name, ex, cancellationToken);
            throw;
        }
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
            AuditActions.Update,
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
        var entity = await _configurationDbContext.IdentityResources
            .FirstOrDefaultAsync(r => r.Name == name, cancellationToken);

        if (entity == null)
        {
            await AuditDeniedAsync(AuditActions.Update, AuditReasonCodes.NotFound, name, name,
                $"Identity Resource '{name}' was not found.", cancellationToken);
            return IdentityResourceEditResult.NotFound;
        }

        if (BuiltInIdentityResourcePolicy.IsProtected(entity))
        {
            var message = BuiltInIdentityResourcePolicy.ProtectedMessage(entity.Name);
            await AuditDeniedAsync(AuditActions.Update, AuditReasonCodes.ProtectedResource, name, displayName ?? name,
                message, cancellationToken);
            return IdentityResourceEditResult.Protected(message);
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
                AuditCategories.IdentityResource, AuditActions.Update, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: name, TargetName: displayName ?? name,
                Details: $"Updated basic settings for Identity Resource '{name}'"), cancellationToken);

            return IdentityResourceEditResult.Succeeded;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.Update, name, displayName ?? name, ex, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Claims

    public Task<IdentityResourceEditResult> AddClaimAsync(string name, string claimType, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.AddClaim,
            name ?? string.Empty,
            name ?? string.Empty,
            () => AddClaimCoreAsync(name ?? string.Empty, claimType ?? string.Empty, cancellationToken),
            cancellationToken);

    private async Task<IdentityResourceEditResult> AddClaimCoreAsync(string name, string claimType, CancellationToken cancellationToken = default)
    {
        // "openid" is a scope, never a user claim. Rejecting it on any resource predates the
        // resource-identity guard below and is kept because it is still correct — the two rules
        // are independent, and this one catches a nonsense edit the other has no opinion on.
        if (string.Equals(claimType, OpenIdClaimType, StringComparison.OrdinalIgnoreCase))
        {
            var message = $"The '{OpenIdClaimType}' claim is protected and cannot be added. It is a scope, not a user claim.";
            await AuditDeniedAsync(AuditActions.AddClaim, AuditReasonCodes.ProtectedResource, name, name, message, cancellationToken);
            return IdentityResourceEditResult.Protected(message);
        }

        var entity = await LoadIdentityResourceAsync(name, asNoTracking: false, cancellationToken);
        if (entity == null)
        {
            await AuditDeniedAsync(AuditActions.AddClaim, AuditReasonCodes.NotFound, name, name,
                $"Identity Resource '{name}' was not found.", cancellationToken);
            return IdentityResourceEditResult.NotFound;
        }

        // Previously absent here while both sibling methods checked it. Without this, claims could
        // be added to a protected resource and then never removed, leaving it in exactly the state
        // the protection exists to prevent and with no recovery path in the UI.
        if (BuiltInIdentityResourcePolicy.IsProtected(entity))
        {
            var message = BuiltInIdentityResourcePolicy.ProtectedMessage(entity.Name);
            await AuditDeniedAsync(AuditActions.AddClaim, AuditReasonCodes.ProtectedResource, name, name, message, cancellationToken);
            return IdentityResourceEditResult.Protected(message);
        }

        if (entity.UserClaims.All(c => c.Type != claimType))
        {
            try
            {
                entity.UserClaims.Add(new IdentityResourceClaim { Type = claimType });
                await _configurationDbContext.SaveChangesAsync(cancellationToken);

                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategories.IdentityResource, AuditActions.AddClaim, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                    TargetId: name, TargetName: name,
                    Details: $"Added claim '{claimType}' to Identity Resource"), cancellationToken);
            }
            catch (Exception ex)
            {
                await AuditFailedAsync(AuditActions.AddClaim, name, name, ex, cancellationToken);
                throw;
            }
        }

        return IdentityResourceEditResult.Succeeded;
    }

    public Task<IdentityResourceEditResult> RemoveClaimAsync(string name, string claimType, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.RemoveClaim,
            name ?? string.Empty,
            name ?? string.Empty,
            () => RemoveClaimCoreAsync(name ?? string.Empty, claimType ?? string.Empty, cancellationToken),
            cancellationToken);

    private async Task<IdentityResourceEditResult> RemoveClaimCoreAsync(string name, string claimType, CancellationToken cancellationToken = default)
    {
        if (string.Equals(claimType, OpenIdClaimType, StringComparison.OrdinalIgnoreCase))
        {
            var message = $"The '{OpenIdClaimType}' claim is protected and cannot be removed.";
            await AuditDeniedAsync(AuditActions.RemoveClaim, AuditReasonCodes.ProtectedResource, name, name, message, cancellationToken);
            return IdentityResourceEditResult.Protected(message);
        }

        var entity = await LoadIdentityResourceAsync(name, asNoTracking: false, cancellationToken);
        if (entity == null)
        {
            await AuditDeniedAsync(AuditActions.RemoveClaim, AuditReasonCodes.NotFound, name, name,
                $"Identity Resource '{name}' was not found.", cancellationToken);
            return IdentityResourceEditResult.NotFound;
        }

        // Checked ahead of the resource-level guard so the operator gets the specific reason
        // ("openid needs sub") rather than the general one, and so the invariant still holds if a
        // deployment ever clears the protection flag on a resource that has one.
        if (BuiltInIdentityResourcePolicy.IsInvariantClaim(entity.Name, claimType))
        {
            var message = BuiltInIdentityResourcePolicy.InvariantClaimMessage(entity.Name, claimType);
            await AuditDeniedAsync(AuditActions.RemoveClaim, AuditReasonCodes.ProtectedResource, name, name, message, cancellationToken);
            return IdentityResourceEditResult.Protected(message);
        }

        if (BuiltInIdentityResourcePolicy.IsProtected(entity))
        {
            var message = BuiltInIdentityResourcePolicy.ProtectedMessage(entity.Name);
            await AuditDeniedAsync(AuditActions.RemoveClaim, AuditReasonCodes.ProtectedResource, name, name, message, cancellationToken);
            return IdentityResourceEditResult.Protected(message);
        }

        var claim = entity.UserClaims.FirstOrDefault(c => c.Type == claimType);
        if (claim == null)
        {
            await AuditDeniedAsync(AuditActions.RemoveClaim, AuditReasonCodes.NotFound, name, name,
                $"Claim '{claimType}' was not found on Identity Resource '{name}'.", cancellationToken);
            return IdentityResourceEditResult.NotFound;
        }

        try
        {
            entity.UserClaims.Remove(claim);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.IdentityResource, AuditActions.RemoveClaim, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: name, TargetName: name,
                Details: $"Removed claim '{claimType}' from Identity Resource"), cancellationToken);

            return IdentityResourceEditResult.Succeeded;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.RemoveClaim, name, name, ex, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Shared infrastructure

    private async Task<IdentityResource?> LoadIdentityResourceAsync(string name, bool asNoTracking, CancellationToken cancellationToken)
    {
        var query = _configurationDbContext.IdentityResources
            .Include(r => r.UserClaims)
            .AsQueryable();

        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

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
            IsProtected = BuiltInIdentityResourcePolicy.IsProtected(entity),
        };
    }

    private Task AuditDeniedAsync(string action, string reasonCode, string targetId, string targetName, string details, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.IdentityResource, action, AuditOutcome.Denied, reasonCode,
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
        const string marker = "IdentityServerProject.Audit.IdentityResource.Failed";
        if (ex.Data.Contains(marker))
        {
            return;
        }

        ex.Data[marker] = true;
        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.IdentityResource, action, AuditOutcome.Failed, AuditReasonCodes.PersistenceFailure,
            TargetId: targetId, TargetName: targetName, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
    }

    #endregion
}
