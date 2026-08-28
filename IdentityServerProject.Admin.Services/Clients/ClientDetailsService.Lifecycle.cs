using System.Data;
using System.Globalization;
using Duende.IdentityServer.EntityFramework.Entities;
using Duende.IdentityServer.EntityFramework.Mappers;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Client = Duende.IdentityServer.EntityFramework.Entities.Client;

namespace IdentityServerProject.Services.Clients;

public partial class ClientDetailsService
{
    public async Task<ClientDetailsModel?> GetClientDetailsAsync(ClientId clientId,
        CancellationToken cancellationToken = default)
    {
        if (clientId.IsEmpty)
            return null;

        Client? client = await _configurationDbContext.Clients
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.RedirectUris)
            .Include(c => c.AllowedCorsOrigins)
            .Include(c => c.ClientSecrets)
            .Include(c => c.AllowedScopes)
            .Include(c => c.Properties)
            .FirstOrDefaultAsync(c => c.ClientId == clientId.Value, cancellationToken);

        if (client is null)
            return null;

        var grantTypesList = client.AllowedGrantTypes.Select(g => g.GrantType).ToList();
        string grantTypesString = grantTypesList.Count > 0 ? string.Join(", ", grantTypesList) : "None";
        (bool canDelete, string? deleteBlockReason) = EvaluateDeleteEligibility(client, _timeProvider.GetUtcNow());

        return new ClientDetailsModel
        {
            ClientId = ClientId.Create(client.ClientId),
            ClientName = string.IsNullOrWhiteSpace(client.ClientName) ? client.ClientId : client.ClientName,
            Description = client.Description,
            ClientType = DeriveClientType(client),
            Enabled = client.Enabled,
            RequirePkce = client.RequirePkce,
            RequireClientSecret = client.RequireClientSecret,
            RequireConsent = client.RequireConsent,
            AllowOfflineAccess = client.AllowOfflineAccess,
            AccessTokenLifetime = TokenLifetime.FromSeconds(client.AccessTokenLifetime),
            AllowedGrantTypes = grantTypesString,
            RedirectUrisCount = client.RedirectUris.Count,
            CorsOriginsCount = client.AllowedCorsOrigins.Count,
            SecretsCount = client.ClientSecrets.Count,
            AllowedScopesCount = client.AllowedScopes.Count,
            AllowedScopes = client.AllowedScopes.OrderBy(s => s.Id).Select(s => s.Scope).ToList(),
            CanDelete = canDelete,
            DeleteBlockReason = deleteBlockReason
        };
    }

    public Task<bool> ToggleClientStatusAsync(ClientId clientId, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.SetEnabled,
            clientId.Value,
            clientId.Value,
            () => ToggleClientStatusCoreAsync(clientId.Value, cancellationToken),
            cancellationToken);

    private async Task<bool> ToggleClientStatusCoreAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return false;

        string targetName = clientId;
        bool found = false;
        bool enabled = false;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        try
        {
            IExecutionStrategy strategy = _configurationDbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _configurationDbContext.ChangeTracker.Clear();
                found = false;
                await using IDbContextTransaction transaction =
                    await _configurationDbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable, cancellationToken);

                Client? client = await _configurationDbContext.Clients
                    .Include(c => c.Properties)
                    .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);
                if (client is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                found = true;
                targetName = client.ClientName ?? clientId;
                client.Enabled = !client.Enabled;
                enabled = client.Enabled;

                ClientProperty? disabledAtProperty =
                    client.Properties.FirstOrDefault(p => p.Key == DisabledAtPropertyKey);
                if (client.Enabled)
                {
                    if (disabledAtProperty is not null) client.Properties.Remove(disabledAtProperty);
                }
                else if (disabledAtProperty is null)
                    client.Properties.Add(new ClientProperty
                    {
                        Key = DisabledAtPropertyKey,
                        Value = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
                    });

                await _configurationDbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });

            if (!found)
                return await DenyToggleStatusNotFoundAsync(clientId, cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.Client, AuditAction.SetEnabled, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                clientId, targetName,
                Details: $"Client status changed to {(enabled ? "Enabled" : "Disabled")}"), cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.SetEnabled, clientId, targetName, ex, cancellationToken);
            throw;
        }
    }

    private async Task<bool> DenyToggleStatusNotFoundAsync(string clientId, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.SetEnabled, AuditReasonCode.NotFound, clientId, clientId,
            $"Client '{clientId}' was not found.", cancellationToken);
        return false;
    }

    public Task<ClientDeleteResult>
        DeleteClientAsync(ClientId clientId, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.Delete,
            clientId.Value,
            clientId.Value,
            () => DeleteClientCoreAsync(clientId.Value, cancellationToken),
            cancellationToken);

    private async Task<ClientDeleteResult> DeleteClientCoreAsync(string clientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return await DenyDeleteClientNotFoundAsync(clientId, cancellationToken);

        string clientName = clientId;
        var outcome = ClientDeleteResult.Failed(
            "Client not found.", AuditReasonCode.NotFound, AdminMutationStatus.NotFound);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        try
        {
            IExecutionStrategy strategy = _configurationDbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _configurationDbContext.ChangeTracker.Clear();
                await using IDbContextTransaction transaction =
                    await _configurationDbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable, cancellationToken);

                Client? client = await _configurationDbContext.Clients
                    .Include(c => c.Properties)
                    .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);
                if (client is null)
                {
                    outcome = ClientDeleteResult.Failed(
                        "Client not found.", AuditReasonCode.NotFound, AdminMutationStatus.NotFound);
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                clientName = client.ClientName ?? client.ClientId;
                (bool canDelete, string? deleteBlockReason) = EvaluateDeleteEligibility(client, now);
                if (!canDelete)
                {
                    AuditReasonCode reasonCode = client.Enabled
                        ? AuditReasonCode.ClientEnabled
                        : AuditReasonCode.RetentionPeriod;
                    outcome = ClientDeleteResult.Failed(deleteBlockReason!, reasonCode);
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                _configurationDbContext.Clients.Remove(client);
                await _configurationDbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                outcome = ClientDeleteResult.Succeeded();
            });

            if (!outcome.Success)
                return await DenyDeleteClientFailedAsync(clientId, clientName, outcome, cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.Client, AuditAction.Delete, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                clientId, clientName,
                Details: $"Deleted client '{clientName}'"), cancellationToken);

            return outcome;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.Delete, clientId, clientName, ex, cancellationToken);
            throw;
        }
    }

    private async Task<ClientDeleteResult> DenyDeleteClientNotFoundAsync(string clientId, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.Delete, AuditReasonCode.NotFound, clientId, clientId,
            "Client not found.", cancellationToken);
        return ClientDeleteResult.Failed(
            "Client not found.", AuditReasonCode.NotFound, AdminMutationStatus.NotFound);
    }

    private async Task<ClientDeleteResult> DenyDeleteClientFailedAsync(
        string clientId, string clientName, ClientDeleteResult outcome, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.Delete, AuditReasonCode.From(outcome.ReasonCode), clientId, clientName,
            outcome.ErrorMessage!, cancellationToken);
        return outcome;
    }

    public Task<AdminMutationResult> UpdateClientBasicsAsync(ClientId clientId, string clientName, string? description,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.UpdateBasics,
            clientId.Value,
            clientName ?? clientId.Value,
            () => UpdateClientBasicsCoreAsync(clientId.Value, clientName ?? string.Empty, description,
                cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> UpdateClientBasicsCoreAsync(string clientId, string clientName,
        string? description, CancellationToken cancellationToken = default)
    {
        clientId = clientId?.Trim() ?? string.Empty;

        string trimmedName = clientName?.Trim() ?? string.Empty;
        string? trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        var errors = new ValidationErrorDictionary();
        if (clientId.Length == 0) errors.AddError("Id", "Client ID is required.");
        if (trimmedName.Length == 0)
            errors.AddError("Input.ClientName", "Client Name is required.");
        else if (trimmedName.Length > ValidationConstants.MaxNameLength)
            errors.AddError("Input.ClientName",
                $"Client Name cannot exceed {ValidationConstants.MaxNameLength} characters.");
        if (trimmedDescription is not null && trimmedDescription.Length > ValidationConstants.MaxDescriptionLength)
            errors.AddError("Input.Description",
                $"Description cannot exceed {ValidationConstants.MaxDescriptionLength} characters.");
        if (errors.HasErrors)
            return await DenyBasicsValidationFailureAsync(clientId, trimmedName, errors, cancellationToken);

        Client? client = await LoadCompleteClientAsync(clientId, true, cancellationToken);

        if (client is null)
            return await DenyBasicsClientNotFoundAsync(clientId, trimmedName, cancellationToken);

        try
        {
            Duende.IdentityServer.Models.Client proposed = client.ToModel();
            proposed.ClientName = trimmedName;
            proposed.Description = trimmedDescription;
            string? validationError = await ValidateClientAsync(proposed, cancellationToken);
            if (validationError is not null)
                return await DenyBasicsInvalidConfigurationAsync(clientId, trimmedName, validationError, cancellationToken);

            Client trackedClient = await _configurationDbContext.Clients
                .FirstAsync(c => c.ClientId == clientId, cancellationToken);
            trackedClient.ClientName = trimmedName;
            trackedClient.Description = trimmedDescription;

            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.Client, AuditAction.UpdateBasics, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                clientId, trimmedName,
                Details: $"Updated basic settings for client '{trimmedName}'"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.UpdateBasics, clientId, trimmedName, ex, cancellationToken);
            throw;
        }
    }

    private async Task<AdminMutationResult> DenyBasicsValidationFailureAsync(
        string clientId, string trimmedName, ValidationErrorDictionary errors, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.UpdateBasics, AuditReasonCode.ValidationFailed, clientId, trimmedName,
            "Client basics validation failed.", cancellationToken);
        return AdminMutationResult.ValidationFailure(errors);
    }

    private async Task<AdminMutationResult> DenyBasicsClientNotFoundAsync(
        string clientId, string trimmedName, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.UpdateBasics, AuditReasonCode.NotFound, clientId, trimmedName,
            $"Client '{clientId}' was not found.", cancellationToken);
        return AdminMutationResult.NotFoundResult();
    }

    private async Task<AdminMutationResult> DenyBasicsInvalidConfigurationAsync(
        string clientId, string trimmedName, string validationError, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.UpdateBasics, AuditReasonCode.ValidationFailed, clientId, trimmedName,
            "Client basics validation failed.", cancellationToken);
        return AdminMutationResult.ValidationFailure("Input.ClientName", validationError);
    }
}