using System.Data;
using Duende.IdentityServer.EntityFramework.Entities;
using Duende.IdentityServer.EntityFramework.Mappers;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Client = Duende.IdentityServer.EntityFramework.Entities.Client;

namespace IdentityServerProject.Services.Clients;

public partial class ClientDetailsService
{
    public async Task<ClientPermissionsModel?> GetClientPermissionsAsync(ClientId clientId,
        CancellationToken cancellationToken = default)
    {
        if (clientId.IsEmpty)
            return null;

        Client? client = await _configurationDbContext.Clients
            .AsNoTracking()
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.AllowedScopes)
            .FirstOrDefaultAsync(c => c.ClientId == clientId.Value, cancellationToken);

        if (client == null)
            return null;

        bool isInteractive = IsInteractiveClient(client);

        List<string> identityScopes = await _configurationDbContext.IdentityResources
            .AsNoTracking()
            .Select(i => i.Name)
            .OrderBy(n => n)
            .ToListAsync(cancellationToken);

        List<string> apiScopes = await _configurationDbContext.ApiScopes
            .AsNoTracking()
            .Select(a => a.Name)
            .OrderBy(n => n)
            .ToListAsync(cancellationToken);

        return new ClientPermissionsModel
        {
            ClientId = ClientId.Create(client.ClientId),
            ClientName = string.IsNullOrWhiteSpace(client.ClientName) ? client.ClientId : client.ClientName,
            IsInteractive = isInteractive,
            AllowedScopes = client.AllowedScopes.OrderBy(s => s.Id).Select(s => s.Scope).ToList(),
            AvailableIdentityScopes = isInteractive ? identityScopes : new List<string>(),
            AvailableApiScopes = apiScopes
        };
    }

    public Task<AdminMutationResult> UpdateClientPermissionsAsync(ClientId clientId, ScopeSet allowedScopes,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.UpdatePermissions,
            clientId.Value,
            clientId.Value,
            () => UpdateClientPermissionsCoreAsync(clientId.Value, allowedScopes ?? ScopeSet.Empty, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> UpdateClientPermissionsCoreAsync(string clientId, ScopeSet allowedScopes,
        CancellationToken cancellationToken = default)
    {
        clientId = clientId?.Trim() ?? string.Empty;
        if (clientId.Length == 0)
            return await DenyPermissionsClientIdRequiredAsync(clientId, cancellationToken);

        IReadOnlyList<string> requestedScopes = allowedScopes.ToValues();
        if (requestedScopes.Any(scope => scope.Length > ValidationConstants.MaxScopeNameLength))
            return await DenyPermissionsScopeTooLongAsync(clientId, cancellationToken);

        var outcome = AdminMutationResult.NotFoundResult();
        string targetName = clientId;
        try
        {
            IExecutionStrategy strategy = _configurationDbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _configurationDbContext.ChangeTracker.Clear();
                await using IDbContextTransaction transaction =
                    await _configurationDbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable, cancellationToken);
                Client? client = await LoadCompleteClientAsync(clientId, false, cancellationToken);
                if (client == null)
                {
                    outcome = AdminMutationResult.NotFoundResult();
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                targetName = client.ClientName ?? clientId;
                var identityScopeSet = new HashSet<string>(
                    await _configurationDbContext.IdentityResources.AsNoTracking()
                        .Select(resource => resource.Name).ToListAsync(cancellationToken),
                    StringComparer.Ordinal);
                var apiScopeSet = new HashSet<string>(
                    await _configurationDbContext.ApiScopes.AsNoTracking()
                        .Select(scope => scope.Name).ToListAsync(cancellationToken),
                    StringComparer.Ordinal);
                var unknownScopes = requestedScopes
                    .Where(scope => !identityScopeSet.Contains(scope) && !apiScopeSet.Contains(scope))
                    .ToList();
                if (unknownScopes.Count > 0)
                {
                    outcome = AdminMutationResult.ValidationFailure(
                        "Input.AllowedScopes",
                        $"Unknown scope(s): {string.Join(", ", unknownScopes)}.");
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                bool isInteractive = IsInteractiveClient(client);
                List<string> finalScopes = isInteractive
                    ? requestedScopes.Union(new[] { "openid" }, StringComparer.Ordinal).ToList()
                    : requestedScopes.Where(scope => !identityScopeSet.Contains(scope)).ToList();
                if (isInteractive && !identityScopeSet.Contains("openid"))
                {
                    outcome = AdminMutationResult.ValidationFailure(
                        "Input.AllowedScopes",
                        "The required 'openid' identity resource is not configured.");
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                Duende.IdentityServer.Models.Client proposed = client.ToModel();
                proposed.AllowedScopes = finalScopes;
                string? validationError = await ValidateClientAsync(proposed, cancellationToken);
                if (validationError != null)
                {
                    outcome = AdminMutationResult.ValidationFailure("Input.AllowedScopes", validationError);
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                ReplaceCollection(client.AllowedScopes, finalScopes, scope => new ClientScope { Scope = scope });
                await _configurationDbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                outcome = AdminMutationResult.Success();
            });
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.UpdatePermissions, clientId, clientId, ex, cancellationToken);
            throw;
        }

        if (!outcome.Succeeded)
            return await DenyPermissionsFailedAsync(clientId, targetName, outcome, cancellationToken);

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.Client, AuditAction.UpdatePermissions, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
            clientId, targetName, Details: "Updated allowed scopes"), cancellationToken);
        return outcome;
    }

    private async Task<AdminMutationResult> DenyPermissionsClientIdRequiredAsync(string clientId, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.UpdatePermissions, AuditReasonCode.ValidationFailed, clientId, clientId,
            "Client permissions validation failed.", cancellationToken);
        return AdminMutationResult.ValidationFailure("Id", "Client ID is required.");
    }

    private async Task<AdminMutationResult> DenyPermissionsScopeTooLongAsync(string clientId, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.UpdatePermissions, AuditReasonCode.ValidationFailed, clientId, clientId,
            "Client permissions validation failed.", cancellationToken);
        return AdminMutationResult.ValidationFailure(
            "Input.AllowedScopes",
            $"Scopes cannot exceed {ValidationConstants.MaxScopeNameLength} characters.");
    }

    private async Task<AdminMutationResult> DenyPermissionsFailedAsync(
        string clientId, string targetName, AdminMutationResult outcome, CancellationToken cancellationToken)
    {
        AuditReasonCode reason = outcome.Status == AdminMutationStatus.NotFound
            ? AuditReasonCode.NotFound
            : AuditReasonCode.ValidationFailed;
        await AuditDeniedAsync(AuditAction.UpdatePermissions, reason, clientId, targetName,
            outcome.ErrorMessage ?? "Client permissions validation failed.", cancellationToken);
        return outcome;
    }
}