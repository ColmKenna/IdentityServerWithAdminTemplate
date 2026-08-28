using System.Data;
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
    public async Task<ClientAuthenticationModel?> GetClientAuthenticationAsync(ClientId clientId,
        CancellationToken cancellationToken = default)
    {
        if (clientId.IsEmpty)
            return null;

        Client? client = await _configurationDbContext.Clients
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.RedirectUris)
            .Include(c => c.PostLogoutRedirectUris)
            .Include(c => c.AllowedCorsOrigins)
            .Include(c => c.Properties)
            .FirstOrDefaultAsync(c => c.ClientId == clientId.Value, cancellationToken);

        if (client is null)
            return null;

        var grantTypes = client.AllowedGrantTypes.Select(g => g.GrantType).OrderBy(g => g, StringComparer.Ordinal)
            .ToList();
        (bool hasDrifted, string? driftDetails) = EvaluatePresetDrift(client, grantTypes);

        return new ClientAuthenticationModel
        {
            ClientId = ClientId.Create(client.ClientId),
            ClientName = string.IsNullOrWhiteSpace(client.ClientName) ? client.ClientId : client.ClientName,
            RequirePkce = client.RequirePkce,
            RequireClientSecret = client.RequireClientSecret,
            GrantTypes = grantTypes,
            RedirectUris = client.RedirectUris.OrderBy(u => u.Id).Select(u => u.RedirectUri).ToList(),
            PostLogoutRedirectUris = client.PostLogoutRedirectUris.OrderBy(u => u.Id)
                .Select(u => u.PostLogoutRedirectUri).ToList(),
            AllowedCorsOrigins = client.AllowedCorsOrigins.OrderBy(o => o.Id).Select(o => o.Origin).ToList(),
            FrontChannelLogoutUri = client.FrontChannelLogoutUri,
            FrontChannelLogoutSessionRequired = client.FrontChannelLogoutSessionRequired,
            BackChannelLogoutUri = client.BackChannelLogoutUri,
            BackChannelLogoutSessionRequired = client.BackChannelLogoutSessionRequired,
            HasDrifted = hasDrifted,
            DriftDetails = driftDetails
        };
    }

    public Task<AdminMutationResult> UpdateClientAuthenticationAsync(ClientId clientId,
        ClientAuthenticationInputModel input, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditAction.UpdateAuthentication,
            clientId.Value,
            clientId.Value,
            () => UpdateClientAuthenticationCoreAsync(clientId.Value, input, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> UpdateClientAuthenticationCoreAsync(string clientId,
        ClientAuthenticationInputModel input, CancellationToken cancellationToken = default)
    {
        clientId = clientId?.Trim() ?? string.Empty;
        var errors = new ValidationErrorDictionary();
        if (clientId.Length == 0) errors.AddError("Id", "Client ID is required.");

        if (input is null)
        {
            errors.AddError("Input", "Authentication settings are required.");
            input = new ClientAuthenticationInputModel();
        }

        List<string> grantTypes = input.GrantTypes?
            .Where(grantType => !string.IsNullOrWhiteSpace(grantType))
            .Select(grantType => grantType.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];
        if (grantTypes.Count == 0)
            errors.AddError("Input.GrantTypes", "At least one grant type must be selected.");
        else if (grantTypes.Any(grantType => grantType.Length > ValidationConstants.MaxGrantTypeLength))
            errors.AddError("Input.GrantTypes",
                $"Grant types cannot exceed {ValidationConstants.MaxGrantTypeLength} characters.");

        List<string> redirectUris = input.RedirectUris?
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

        if (redirectUris.Any(uri =>
                !UriValidationHelper.IsValidHttpOrHttpsUri(uri, ValidationConstants.MaxClientRedirectUriLength)))
            errors.AddError("Input.RedirectUris",
                "Each Redirect URI must be an absolute HTTP or HTTPS URL within the configured length limit.");

        List<string> postLogoutUris = input.PostLogoutRedirectUris?
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

        if (postLogoutUris.Any(uri =>
                !UriValidationHelper.IsValidHttpOrHttpsUri(uri,
                    ValidationConstants.MaxClientPostLogoutRedirectUriLength)))
            errors.AddError("Input.PostLogoutRedirectUris",
                "Each Post-Logout Redirect URI must be an absolute HTTP or HTTPS URL within the configured length limit.");

        List<string> rawCorsOrigins = input.CorsOrigins?
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .ToList() ?? [];
        var corsOrigins = new List<string>();
        foreach (string origin in rawCorsOrigins)
        {
            if (!UriValidationHelper.TryNormalizeCorsOrigin(origin, ValidationConstants.MaxClientCorsOriginLength,
                    out string normalizedOrigin))
            {
                errors.AddError("Input.CorsOrigins",
                    "Each CORS Origin must contain only an HTTP or HTTPS scheme, host, and optional port.");
                break;
            }

            if (!corsOrigins.Contains(normalizedOrigin, StringComparer.OrdinalIgnoreCase))
                corsOrigins.Add(normalizedOrigin);
        }

        if (!string.IsNullOrWhiteSpace(input.FrontChannelLogoutUri) &&
            !UriValidationHelper.IsValidHttpOrHttpsUri(input.FrontChannelLogoutUri,
                ValidationConstants.MaxLogoutUriLength))
            errors.AddError("Input.FrontChannelLogoutUri",
                "Front-channel logout URI must be an absolute HTTP or HTTPS URL within the configured length limit.");

        if (!string.IsNullOrWhiteSpace(input.BackChannelLogoutUri) &&
            !UriValidationHelper.IsValidHttpOrHttpsUri(input.BackChannelLogoutUri,
                ValidationConstants.MaxLogoutUriLength))
            errors.AddError("Input.BackChannelLogoutUri",
                "Back-channel logout URI must be an absolute HTTP or HTTPS URL within the configured length limit.");

        if (errors.HasErrors)
            return await DenyAuthenticationValidationFailureAsync(clientId, errors, cancellationToken);

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
                if (client is null)
                {
                    outcome = AdminMutationResult.NotFoundResult();
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                targetName = client.ClientName ?? clientId;
                Duende.IdentityServer.Models.Client proposed = client.ToModel();
                proposed.RequirePkce = input.RequirePkce;
                proposed.RequireClientSecret = input.RequireClientSecret;
                proposed.AllowedGrantTypes = grantTypes;
                proposed.RedirectUris = redirectUris;
                proposed.PostLogoutRedirectUris = postLogoutUris;
                proposed.AllowedCorsOrigins = corsOrigins;
                proposed.FrontChannelLogoutUri = input.FrontChannelLogoutUri;
                proposed.FrontChannelLogoutSessionRequired = input.FrontChannelLogoutSessionRequired;
                proposed.BackChannelLogoutUri = input.BackChannelLogoutUri;
                proposed.BackChannelLogoutSessionRequired = input.BackChannelLogoutSessionRequired;
                string? validationError = await ValidateClientAsync(proposed, cancellationToken);
                if (validationError is not null)
                {
                    outcome = AdminMutationResult.ValidationFailure("Input.GrantTypes", validationError);
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                client.RequirePkce = input.RequirePkce;
                client.RequireClientSecret = input.RequireClientSecret;
                ReplaceCollection(client.AllowedGrantTypes, grantTypes,
                    grantType => new ClientGrantType { GrantType = grantType });
                ReplaceCollection(client.RedirectUris, redirectUris,
                    uri => new ClientRedirectUri { RedirectUri = uri });
                ReplaceCollection(client.PostLogoutRedirectUris, postLogoutUris,
                    uri => new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = uri });
                ReplaceCollection(client.AllowedCorsOrigins, corsOrigins,
                    origin => new ClientCorsOrigin { Origin = origin });
                client.FrontChannelLogoutUri = input.FrontChannelLogoutUri;
                client.FrontChannelLogoutSessionRequired = input.FrontChannelLogoutSessionRequired;
                client.BackChannelLogoutUri = input.BackChannelLogoutUri;
                client.BackChannelLogoutSessionRequired = input.BackChannelLogoutSessionRequired;
                await _configurationDbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                outcome = AdminMutationResult.Success();
            });
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.UpdateAuthentication, clientId, clientId, ex, cancellationToken);
            throw;
        }

        if (!outcome.Succeeded)
            return await DenyAuthenticationFailedAsync(clientId, targetName, outcome, cancellationToken);

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.Client, AuditAction.UpdateAuthentication, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
            clientId, targetName, Details: "Updated authentication settings"), cancellationToken);
        return outcome;
    }

    private async Task<AdminMutationResult> DenyAuthenticationValidationFailureAsync(
        string clientId, ValidationErrorDictionary errors, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditAction.UpdateAuthentication, AuditReasonCode.ValidationFailed, clientId, clientId,
            "Client authentication validation failed.", cancellationToken);
        return AdminMutationResult.ValidationFailure(errors);
    }

    private async Task<AdminMutationResult> DenyAuthenticationFailedAsync(
        string clientId, string targetName, AdminMutationResult outcome, CancellationToken cancellationToken)
    {
        AuditReasonCode reason = outcome.Status == AdminMutationStatus.NotFound
            ? AuditReasonCode.NotFound
            : AuditReasonCode.ValidationFailed;
        await AuditDeniedAsync(AuditAction.UpdateAuthentication, reason, clientId, targetName,
            outcome.ErrorMessage ?? "Client authentication validation failed.", cancellationToken);
        return outcome;
    }
}