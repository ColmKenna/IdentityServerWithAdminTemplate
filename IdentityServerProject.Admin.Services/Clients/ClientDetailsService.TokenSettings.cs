using Duende.IdentityServer.EntityFramework.Mappers;
using Duende.IdentityServer.Models;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Client = Duende.IdentityServer.EntityFramework.Entities.Client;

namespace IdentityServerProject.Services.Clients;

public partial class ClientDetailsService
{
    public async Task<ClientTokenSettingsModel?> GetClientTokenSettingsAsync(ClientId clientId,
        CancellationToken cancellationToken = default)
    {
        if (clientId.IsEmpty)
            return null;

        Client? client = await _configurationDbContext.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientId == clientId.Value, cancellationToken);

        if (client is null)
            return null;

        return new ClientTokenSettingsModel
        {
            ClientId = ClientId.Create(client.ClientId),
            ClientName = string.IsNullOrWhiteSpace(client.ClientName) ? client.ClientId : client.ClientName,
            AccessTokenLifetime = TokenLifetime.FromSeconds(client.AccessTokenLifetime),
            IdentityTokenLifetime = TokenLifetime.FromSeconds(client.IdentityTokenLifetime),
            RequireConsent = client.RequireConsent,
            AllowOfflineAccess = client.AllowOfflineAccess,
            RefreshToken = new RefreshTokenSettings
            {
                Usage = (TokenUsage)client.RefreshTokenUsage,
                Expiration = (TokenExpiration)client.RefreshTokenExpiration,
                AbsoluteLifetime = TokenLifetime.FromSeconds(client.AbsoluteRefreshTokenLifetime),
                SlidingLifetime = TokenLifetime.FromSeconds(client.SlidingRefreshTokenLifetime)
            }
        };
    }

    public Task<AdminMutationResult> UpdateClientTokenSettingsAsync(ClientId clientId,
        ClientTokenSettingsInputModel input, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.UpdateTokenSettings,
            clientId.Value,
            clientId.Value,
            () => UpdateClientTokenSettingsCoreAsync(clientId.Value, input, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> UpdateClientTokenSettingsCoreAsync(string clientId,
        ClientTokenSettingsInputModel input, CancellationToken cancellationToken = default)
    {
        clientId = clientId?.Trim() ?? string.Empty;
        var errors = new ValidationErrorDictionary();
        if (clientId.Length == 0) errors.AddError("Id", "Client ID is required.");
        if (input is null)
            errors.AddError("Input", "Token settings are required.");
        else
        {
            if (!input.AccessTokenLifetime.IsValidAccessToken)
                errors.AddError("Input.AccessTokenLifetime",
                    $"Access Token Lifetime must be between {ValidationConstants.MinAccessTokenLifetime} and {ValidationConstants.MaxAccessTokenLifetime} seconds.");
            if (!input.IdentityTokenLifetime.IsValidIdentityToken)
                errors.AddError("Input.IdentityTokenLifetime",
                    $"Identity Token Lifetime must be between {ValidationConstants.MinIdentityTokenLifetime} and {ValidationConstants.MaxIdentityTokenLifetime} seconds.");

            if (input.AllowOfflineAccess)
            {
                RefreshTokenSettings refresh = input.RefreshToken ?? new RefreshTokenSettings();
                if (!refresh.IsAbsoluteLifetimeValid)
                    errors.AddError("Input.AbsoluteRefreshTokenLifetime",
                        $"Absolute Refresh Token Lifetime must be between {ValidationConstants.MinRefreshTokenLifetime} and {ValidationConstants.MaxAbsoluteRefreshTokenLifetime} seconds.");

                if (!refresh.IsSlidingLifetimeValid)
                    errors.AddError("Input.SlidingRefreshTokenLifetime",
                        $"Sliding Refresh Token Lifetime must be between {ValidationConstants.MinRefreshTokenLifetime} and {ValidationConstants.MaxSlidingRefreshTokenLifetime} seconds.");

                if (!refresh.IsSlidingValid)
                    errors.AddError("Input.SlidingRefreshTokenLifetime",
                        "Sliding Refresh Token Lifetime cannot exceed the Absolute Refresh Token Lifetime.");
            }
        }

        if (errors.HasErrors)
            return await DenyTokenSettingsValidationFailureAsync(clientId, errors, cancellationToken);

        Client? client = await LoadCompleteClientAsync(clientId, true, cancellationToken);

        if (client is null)
            return await DenyTokenSettingsClientNotFoundAsync(clientId, cancellationToken);

        try
        {
            RefreshTokenSettings refresh = input!.RefreshToken ?? new RefreshTokenSettings();
            Duende.IdentityServer.Models.Client proposed = client.ToModel();
            proposed.AccessTokenLifetime = input.AccessTokenLifetime.Seconds;
            proposed.IdentityTokenLifetime = input.IdentityTokenLifetime.Seconds;
            proposed.RequireConsent = input.RequireConsent;
            proposed.AllowOfflineAccess = input.AllowOfflineAccess;
            if (input.AllowOfflineAccess)
            {
                proposed.RefreshTokenUsage = refresh.Usage;
                proposed.RefreshTokenExpiration = refresh.Expiration;
                proposed.AbsoluteRefreshTokenLifetime = refresh.AbsoluteLifetime.Seconds;
                proposed.SlidingRefreshTokenLifetime = refresh.SlidingLifetime.Seconds;
            }

            string? validationError = await ValidateClientAsync(proposed, cancellationToken);
            if (validationError is not null)
                return await DenyTokenSettingsInvalidConfigurationAsync(clientId, client, validationError, cancellationToken);

            Client trackedClient = await _configurationDbContext.Clients
                .FirstAsync(c => c.ClientId == clientId, cancellationToken);
            var oldValues = new ClientTokenSettingsAuditValue(
                TokenLifetime.FromSeconds(trackedClient.AccessTokenLifetime),
                TokenLifetime.FromSeconds(trackedClient.IdentityTokenLifetime),
                trackedClient.RequireConsent,
                trackedClient.AllowOfflineAccess,
                new RefreshTokenSettings
                {
                    Usage = (TokenUsage)trackedClient.RefreshTokenUsage,
                    Expiration = (TokenExpiration)trackedClient.RefreshTokenExpiration,
                    AbsoluteLifetime = TokenLifetime.FromSeconds(trackedClient.AbsoluteRefreshTokenLifetime),
                    SlidingLifetime = TokenLifetime.FromSeconds(trackedClient.SlidingRefreshTokenLifetime)
                });
            trackedClient.AccessTokenLifetime = input.AccessTokenLifetime.Seconds;
            trackedClient.IdentityTokenLifetime = input.IdentityTokenLifetime.Seconds;
            trackedClient.RequireConsent = input.RequireConsent;
            trackedClient.AllowOfflineAccess = input.AllowOfflineAccess;
            if (input.AllowOfflineAccess)
            {
                trackedClient.RefreshTokenUsage = (int)refresh.Usage;
                trackedClient.RefreshTokenExpiration = (int)refresh.Expiration;
                trackedClient.AbsoluteRefreshTokenLifetime = refresh.AbsoluteLifetime.Seconds;
                trackedClient.SlidingRefreshTokenLifetime = refresh.SlidingLifetime.Seconds;
            }

            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.Client, AuditAction.UpdateTokenSettings, AuditOutcome.Succeeded,
                AuditReasonCode.Succeeded,
                clientId, client.ClientName ?? clientId,
                oldValues,
                new ClientTokenSettingsAuditValue(
                    TokenLifetime.FromSeconds(trackedClient.AccessTokenLifetime),
                    TokenLifetime.FromSeconds(trackedClient.IdentityTokenLifetime),
                    trackedClient.RequireConsent,
                    trackedClient.AllowOfflineAccess,
                    new RefreshTokenSettings
                    {
                        Usage = (TokenUsage)trackedClient.RefreshTokenUsage,
                        Expiration = (TokenExpiration)trackedClient.RefreshTokenExpiration,
                        AbsoluteLifetime = TokenLifetime.FromSeconds(trackedClient.AbsoluteRefreshTokenLifetime),
                        SlidingLifetime = TokenLifetime.FromSeconds(trackedClient.SlidingRefreshTokenLifetime)
                    }
                ),
                "Updated token and consent settings"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.UpdateTokenSettings, clientId, client.ClientName ?? clientId, ex,
                cancellationToken);
            throw;
        }
    }

    private async Task<AdminMutationResult> DenyTokenSettingsValidationFailureAsync(
        string clientId, ValidationErrorDictionary errors, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.UpdateTokenSettings, AuditReasonCode.ValidationFailed, clientId, clientId,
            "Client token settings validation failed.", cancellationToken);
        return AdminMutationResult.ValidationFailure(errors);
    }

    private async Task<AdminMutationResult> DenyTokenSettingsClientNotFoundAsync(string clientId, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.UpdateTokenSettings, AuditReasonCode.NotFound, clientId, clientId,
            $"Client '{clientId}' was not found.", cancellationToken);
        return AdminMutationResult.NotFoundResult();
    }

    private async Task<AdminMutationResult> DenyTokenSettingsInvalidConfigurationAsync(
        string clientId, Client client, string validationError, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.UpdateTokenSettings, AuditReasonCode.ValidationFailed, clientId,
            client.ClientName ?? clientId, "Client token settings validation failed.", cancellationToken);
        return AdminMutationResult.ValidationFailure("Input.AccessTokenLifetime", validationError);
    }
}