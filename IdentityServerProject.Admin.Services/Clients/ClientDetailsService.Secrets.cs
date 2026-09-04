using System.Data;
using Duende.IdentityModel;
using Duende.IdentityServer.EntityFramework.Entities;
using Duende.IdentityServer.Models;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Secrets;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Client = Duende.IdentityServer.EntityFramework.Entities.Client;

namespace IdentityServerProject.Services.Clients;

public partial class ClientDetailsService
{
    public async Task<ClientSecretsModel?> GetClientSecretsAsync(ClientId clientId,
        CancellationToken cancellationToken = default)
    {
        if (clientId.IsEmpty)
            return null;

        Client? client = await _configurationDbContext.Clients
            .AsNoTracking()
            .Include(c => c.ClientSecrets)
            .FirstOrDefaultAsync(c => c.ClientId == clientId.Value, cancellationToken);

        if (client is null)
            return null;

        return new ClientSecretsModel
        {
            ClientId = ClientId.Create(client.ClientId),
            ClientName = string.IsNullOrWhiteSpace(client.ClientName) ? client.ClientId : client.ClientName,
            RequireClientSecret = client.RequireClientSecret,
            Secrets = client.ClientSecrets
                .OrderBy(s => s.Id)
                .Select(s => new ClientSecretSummary
                {
                    Id = s.Id,
                    Description = s.Description,
                    Created = s.Created,
                    Expiration = s.Expiration
                })
                .ToList()
        };
    }

    public Task<ClientSecretGenerateResult> GenerateClientSecretAsync(ClientId clientId, string? description,
        DateTime? expiration = null, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.GenerateSecret,
            clientId.Value,
            clientId.Value,
            () => GenerateClientSecretCoreAsync(clientId.Value, description, expiration, cancellationToken),
            cancellationToken);

    private async Task<ClientSecretGenerateResult> GenerateClientSecretCoreAsync(string clientId, string? description,
        DateTime? expiration = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return await DenySecretClientNotFoundAsync(clientId, cancellationToken);

        string? trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is not null &&
            trimmedDescription.Length > ValidationConstants.MaxClientSecretDescriptionLength)
            return await DenySecretDescriptionTooLongAsync(clientId, cancellationToken);

        if (expiration.HasValue && expiration.Value.ToUniversalTime() <= DateTime.UtcNow)
            return await DenySecretExpirationInPastAsync(clientId, cancellationToken);

        string targetName = clientId;
        var outcome = ClientSecretGenerateResult.Failed("Client not found.");
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
                    .Include(c => c.ClientSecrets)
                    .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);

                if (client is null)
                {
                    outcome = ClientSecretGenerateResult.Failed("Client not found.");
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                targetName = client.ClientName ?? clientId;
                string plaintextSecret = CryptoRandom.CreateUniqueId();
                client.ClientSecrets.Add(new ClientSecret
                {
                    Description = trimmedDescription,
                    Value = plaintextSecret.Sha256(),
                    Type = SecretType.SharedSecret.ToSecretTypeValue(),
                    Expiration = expiration?.ToUniversalTime(),
                    Created = DateTime.UtcNow
                });

                await _configurationDbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                outcome = ClientSecretGenerateResult.Succeeded(plaintextSecret);
            });

            if (!outcome.Success)
                return await DenySecretGenerationClientNotFoundAsync(clientId, targetName, cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.Client, AuditAction.GenerateSecret, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                clientId, targetName,
                Details: "Generated new client secret"), cancellationToken);

            return outcome;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.GenerateSecret, clientId, targetName, ex, cancellationToken);
            throw;
        }
    }

    private async Task<ClientSecretGenerateResult> DenySecretClientNotFoundAsync(string clientId, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.GenerateSecret, AuditReasonCode.NotFound, clientId, clientId,
            "Client not found.", cancellationToken);
        return ClientSecretGenerateResult.Failed("Client not found.");
    }

    private async Task<ClientSecretGenerateResult> DenySecretDescriptionTooLongAsync(string clientId, CancellationToken cancellationToken)
    {
        string message =
            $"Secret description cannot exceed {ValidationConstants.MaxClientSecretDescriptionLength} characters.";
        await AuditDeniedAsync(AuditAction.GenerateSecret, AuditReasonCode.ValidationFailed, clientId, clientId,
            message, cancellationToken);
        return ClientSecretGenerateResult.ValidationFailure("Description", message);
    }

    private async Task<ClientSecretGenerateResult> DenySecretExpirationInPastAsync(string clientId, CancellationToken cancellationToken)
    {
        const string message = "Expiration date must be in the future.";
        await AuditDeniedAsync(AuditAction.GenerateSecret, AuditReasonCode.ValidationFailed, clientId, clientId,
            message, cancellationToken);
        return ClientSecretGenerateResult.ValidationFailure("Expiration", message);
    }

    private async Task<ClientSecretGenerateResult> DenySecretGenerationClientNotFoundAsync(
        string clientId, string targetName, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.GenerateSecret, AuditReasonCode.NotFound, clientId, targetName,
            "Client not found.", cancellationToken);
        return ClientSecretGenerateResult.Failed("Client not found.");
    }

    public Task<ClientSecretRevokeResult> RevokeClientSecretAsync(ClientId clientId, int secretId,
        CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.RevokeSecret,
            clientId.Value,
            clientId.Value,
            () => RevokeClientSecretCoreAsync(clientId.Value, secretId, cancellationToken),
            cancellationToken);

    private async Task<ClientSecretRevokeResult> RevokeClientSecretCoreAsync(string clientId, int secretId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return await DenyRevokeSecretClientNotFoundAsync(clientId, cancellationToken);

        string targetName = clientId;
        var outcome = ClientSecretRevokeResult.Failed(
            "Client not found.", AuditReasonCode.NotFound, AdminMutationStatus.NotFound);
        DateTimeOffset utcNow = _timeProvider.GetUtcNow();
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
                    .Include(c => c.ClientSecrets)
                    .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);

                if (client is null)
                {
                    outcome = ClientSecretRevokeResult.Failed(
                        "Client not found.", AuditReasonCode.NotFound, AdminMutationStatus.NotFound);
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                targetName = client.ClientName ?? clientId;
                ClientSecret? secret = client.ClientSecrets.FirstOrDefault(s => s.Id == secretId);
                if (secret is null)
                {
                    outcome = ClientSecretRevokeResult.Failed(
                        "Secret not found.", AuditReasonCode.NotFound, AdminMutationStatus.NotFound);
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                bool hasUsableReplacement = client.ClientSecrets.Any(s =>
                    s.Id != secretId && (!s.Expiration.HasValue || s.Expiration.Value > utcNow.UtcDateTime));
                if (client.RequireClientSecret && !hasUsableReplacement)
                {
                    string message =
                        "This is the last usable secret on a confidential client and cannot be revoked. Generate a usable replacement secret first, or disable the client's secret requirement.";
                    outcome = ClientSecretRevokeResult.Failed(message, AuditReasonCode.LastUsableSecret);
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                client.ClientSecrets.Remove(secret);
                await _configurationDbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                outcome = ClientSecretRevokeResult.Succeeded();
            });

            if (!outcome.Success)
                return await DenyRevokeSecretFailedAsync(clientId, targetName, outcome, cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.Client, AuditAction.RevokeSecret, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                clientId, targetName,
                Details: "Revoked client secret"), cancellationToken);

            return outcome;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.RevokeSecret, clientId, targetName, ex, cancellationToken);
            throw;
        }
    }

    private async Task<ClientSecretRevokeResult> DenyRevokeSecretClientNotFoundAsync(string clientId, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.RevokeSecret, AuditReasonCode.NotFound, clientId, clientId,
            "Client not found.", cancellationToken);
        return ClientSecretRevokeResult.Failed(
            "Client not found.", AuditReasonCode.NotFound, AdminMutationStatus.NotFound);
    }

    private async Task<ClientSecretRevokeResult> DenyRevokeSecretFailedAsync(
        string clientId, string targetName, ClientSecretRevokeResult outcome, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.RevokeSecret, AuditReasonCode.From(outcome.ReasonCode), clientId, targetName,
            outcome.ErrorMessage!, cancellationToken);
        return outcome;
    }
}