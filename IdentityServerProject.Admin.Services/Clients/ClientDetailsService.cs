using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityModel;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Duende.IdentityServer.EntityFramework.Mappers;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Validation;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using static Duende.IdentityServer.Models.HashExtensions;
using Client = Duende.IdentityServer.EntityFramework.Entities.Client;

namespace IdentityServerProject.Services.Clients;

public partial class ClientDetailsService : IClientDetailsService
{
    private const string GrantTypeAuthorizationCode = "authorization_code";
    private const string GrantTypeClientCredentials = "client_credentials";
    private const string GrantTypeHybrid = "hybrid";
    private const string GrantTypeImplicit = "implicit";
    private const string GrantTypeDeviceCode = "urn:ietf:params:oauth:grant-type:device_code";
    private const string SecretTypeShared = "SharedSecret";

    public const string DisabledAtPropertyKey = "admin:disabledAt";
    public const int MinimumDisabledDaysBeforeDelete = 90;

    private readonly ConfigurationDbContext _configurationDbContext;
    private readonly IAuditWriter _auditWriter;
    private readonly IClientConfigurationValidator _clientConfigurationValidator;
    private readonly TimeProvider _timeProvider;

    public ClientDetailsService(
        ConfigurationDbContext configurationDbContext,
        IAuditWriter auditWriter,
        IClientConfigurationValidator clientConfigurationValidator,
        TimeProvider timeProvider)
    {
        _configurationDbContext = configurationDbContext;
        _auditWriter = auditWriter;
        _clientConfigurationValidator = clientConfigurationValidator;
        _timeProvider = timeProvider;
    }

    #region Overview and lifecycle

    public async Task<ClientDetailsModel?> GetClientDetailsAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        var client = await _configurationDbContext.Clients
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.RedirectUris)
            .Include(c => c.AllowedCorsOrigins)
            .Include(c => c.ClientSecrets)
            .Include(c => c.AllowedScopes)
            .Include(c => c.Properties)
            .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);

        if (client == null)
        {
            return null;
        }

        var grantTypesList = client.AllowedGrantTypes.Select(g => g.GrantType).ToList();
        var grantTypesString = grantTypesList.Count > 0 ? string.Join(", ", grantTypesList) : "None";
        var (canDelete, deleteBlockReason) = EvaluateDeleteEligibility(client, _timeProvider.GetUtcNow());

        return new ClientDetailsModel
        {
            ClientId = client.ClientId,
            ClientName = string.IsNullOrWhiteSpace(client.ClientName) ? client.ClientId : client.ClientName,
            Description = client.Description,
            ClientType = DeriveClientType(client),
            Enabled = client.Enabled,
            RequirePkce = client.RequirePkce,
            RequireClientSecret = client.RequireClientSecret,
            RequireConsent = client.RequireConsent,
            AllowOfflineAccess = client.AllowOfflineAccess,
            AccessTokenLifetime = client.AccessTokenLifetime,
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

    public Task<bool> ToggleClientStatusAsync(string clientId, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.SetEnabled,
            clientId ?? string.Empty,
            clientId ?? string.Empty,
            () => ToggleClientStatusCoreAsync(clientId ?? string.Empty, cancellationToken),
            cancellationToken);

    private async Task<bool> ToggleClientStatusCoreAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return false;
        }

        var targetName = clientId;
        var found = false;
        var enabled = false;
        var now = _timeProvider.GetUtcNow();
        try
        {
            var strategy = _configurationDbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _configurationDbContext.ChangeTracker.Clear();
                found = false;
                await using var transaction = await _configurationDbContext.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable, cancellationToken);

                var client = await _configurationDbContext.Clients
                    .Include(c => c.Properties)
                    .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);
                if (client == null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                found = true;
                targetName = client.ClientName ?? clientId;
                client.Enabled = !client.Enabled;
                enabled = client.Enabled;

                var disabledAtProperty = client.Properties.FirstOrDefault(p => p.Key == DisabledAtPropertyKey);
                if (client.Enabled)
                {
                    if (disabledAtProperty != null)
                    {
                        client.Properties.Remove(disabledAtProperty);
                    }
                }
                else if (disabledAtProperty == null)
                {
                    client.Properties.Add(new ClientProperty
                    {
                        Key = DisabledAtPropertyKey,
                        Value = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
                    });
                }

                await _configurationDbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });

            if (!found)
            {
                await AuditDeniedAsync(AuditActions.SetEnabled, AuditReasonCodes.NotFound, clientId, clientId,
                    $"Client '{clientId}' was not found.", cancellationToken);
                return false;
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.Client, AuditActions.SetEnabled, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: clientId, TargetName: targetName,
                Details: $"Client status changed to {(enabled ? "Enabled" : "Disabled")}"), cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.SetEnabled, clientId, targetName, ex, cancellationToken);
            throw;
        }
    }

    public Task<ClientDeleteResult> DeleteClientAsync(string clientId, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.Delete,
            clientId ?? string.Empty,
            clientId ?? string.Empty,
            () => DeleteClientCoreAsync(clientId ?? string.Empty, cancellationToken),
            cancellationToken);

    private async Task<ClientDeleteResult> DeleteClientCoreAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            await AuditDeniedAsync(AuditActions.Delete, AuditReasonCodes.NotFound, clientId, clientId,
                "Client not found.", cancellationToken);
            return ClientDeleteResult.Failed(
                "Client not found.", AuditReasonCodes.NotFound, AdminMutationStatus.NotFound);
        }

        var clientName = clientId;
        var outcome = ClientDeleteResult.Failed(
            "Client not found.", AuditReasonCodes.NotFound, AdminMutationStatus.NotFound);
        var now = _timeProvider.GetUtcNow();
        try
        {
            var strategy = _configurationDbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _configurationDbContext.ChangeTracker.Clear();
                await using var transaction = await _configurationDbContext.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable, cancellationToken);

                var client = await _configurationDbContext.Clients
                    .Include(c => c.Properties)
                    .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);
                if (client == null)
                {
                    outcome = ClientDeleteResult.Failed(
                        "Client not found.", AuditReasonCodes.NotFound, AdminMutationStatus.NotFound);
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                clientName = client.ClientName ?? client.ClientId;
                var (canDelete, deleteBlockReason) = EvaluateDeleteEligibility(client, now);
                if (!canDelete)
                {
                    var reasonCode = client.Enabled
                        ? AuditReasonCodes.ClientEnabled
                        : AuditReasonCodes.RetentionPeriod;
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
            {
                await AuditDeniedAsync(AuditActions.Delete, outcome.ReasonCode!, clientId, clientName,
                    outcome.ErrorMessage!, cancellationToken);
                return outcome;
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.Client, AuditActions.Delete, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: clientId, TargetName: clientName,
                Details: $"Deleted client '{clientName}'"), cancellationToken);

            return outcome;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.Delete, clientId, clientName, ex, cancellationToken);
            throw;
        }
    }

    public Task<AdminMutationResult> UpdateClientBasicsAsync(string clientId, string clientName, string? description, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.UpdateBasics,
            clientId ?? string.Empty,
            clientName ?? clientId ?? string.Empty,
            () => UpdateClientBasicsCoreAsync(clientId ?? string.Empty, clientName ?? string.Empty, description, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> UpdateClientBasicsCoreAsync(string clientId, string clientName, string? description, CancellationToken cancellationToken = default)
    {
        clientId = clientId?.Trim() ?? string.Empty;

        var trimmedName = clientName?.Trim() ?? string.Empty;
        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        var errors = new Dictionary<string, string[]>();
        if (clientId.Length == 0)
        {
            errors["Id"] = new[] { "Client ID is required." };
        }
        if (trimmedName.Length == 0)
        {
            errors["Input.ClientName"] = new[] { "Client Name is required." };
        }
        else if (trimmedName.Length > ValidationConstants.MaxNameLength)
        {
            errors["Input.ClientName"] = new[] { $"Client Name cannot exceed {ValidationConstants.MaxNameLength} characters." };
        }
        if (trimmedDescription != null && trimmedDescription.Length > ValidationConstants.MaxDescriptionLength)
        {
            errors["Input.Description"] = new[] { $"Description cannot exceed {ValidationConstants.MaxDescriptionLength} characters." };
        }
        if (errors.Count > 0)
        {
            await AuditDeniedAsync(AuditActions.UpdateBasics, AuditReasonCodes.ValidationFailed, clientId, trimmedName,
                "Client basics validation failed.", cancellationToken);
            return AdminMutationResult.ValidationFailure(errors);
        }

        var client = await LoadCompleteClientAsync(clientId, asNoTracking: true, cancellationToken);

        if (client == null)
        {
            await AuditDeniedAsync(AuditActions.UpdateBasics, AuditReasonCodes.NotFound, clientId, trimmedName,
                $"Client '{clientId}' was not found.", cancellationToken);
            return AdminMutationResult.NotFoundResult();
        }

        try
        {
            var proposed = client.ToModel();
            proposed.ClientName = trimmedName;
            proposed.Description = trimmedDescription;
            var validationError = await ValidateClientAsync(proposed, cancellationToken);
            if (validationError != null)
            {
                await AuditDeniedAsync(AuditActions.UpdateBasics, AuditReasonCodes.ValidationFailed, clientId, trimmedName,
                    "Client basics validation failed.", cancellationToken);
                return AdminMutationResult.ValidationFailure("Input.ClientName", validationError);
            }

            var trackedClient = await _configurationDbContext.Clients
                .FirstAsync(c => c.ClientId == clientId, cancellationToken);
            trackedClient.ClientName = trimmedName;
            trackedClient.Description = trimmedDescription;

            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.Client, AuditActions.UpdateBasics, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: clientId, TargetName: trimmedName,
                Details: $"Updated basic settings for client '{trimmedName}'"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.UpdateBasics, clientId, trimmedName, ex, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Authentication

    public async Task<ClientAuthenticationModel?> GetClientAuthenticationAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        var client = await _configurationDbContext.Clients
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.RedirectUris)
            .Include(c => c.PostLogoutRedirectUris)
            .Include(c => c.AllowedCorsOrigins)
            .Include(c => c.Properties)
            .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);

        if (client == null)
        {
            return null;
        }

        var grantTypes = client.AllowedGrantTypes.Select(g => g.GrantType).OrderBy(g => g, StringComparer.Ordinal).ToList();
        var (hasDrifted, driftDetails) = EvaluatePresetDrift(client, grantTypes);

        return new ClientAuthenticationModel
        {
            ClientId = client.ClientId,
            ClientName = string.IsNullOrWhiteSpace(client.ClientName) ? client.ClientId : client.ClientName,
            RequirePkce = client.RequirePkce,
            RequireClientSecret = client.RequireClientSecret,
            GrantTypes = grantTypes,
            RedirectUris = client.RedirectUris.OrderBy(u => u.Id).Select(u => u.RedirectUri).ToList(),
            PostLogoutRedirectUris = client.PostLogoutRedirectUris.OrderBy(u => u.Id).Select(u => u.PostLogoutRedirectUri).ToList(),
            AllowedCorsOrigins = client.AllowedCorsOrigins.OrderBy(o => o.Id).Select(o => o.Origin).ToList(),
            FrontChannelLogoutUri = client.FrontChannelLogoutUri,
            FrontChannelLogoutSessionRequired = client.FrontChannelLogoutSessionRequired,
            BackChannelLogoutUri = client.BackChannelLogoutUri,
            BackChannelLogoutSessionRequired = client.BackChannelLogoutSessionRequired,
            HasDrifted = hasDrifted,
            DriftDetails = driftDetails
        };
    }

    public Task<AdminMutationResult> UpdateClientAuthenticationAsync(string clientId, ClientAuthenticationInputModel input, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.UpdateAuthentication,
            clientId ?? string.Empty,
            clientId ?? string.Empty,
            () => UpdateClientAuthenticationCoreAsync(clientId ?? string.Empty, input, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> UpdateClientAuthenticationCoreAsync(string clientId, ClientAuthenticationInputModel input, CancellationToken cancellationToken = default)
    {
        clientId = clientId?.Trim() ?? string.Empty;
        var errors = new Dictionary<string, string[]>();
        if (clientId.Length == 0)
        {
            errors["Id"] = new[] { "Client ID is required." };
        }

        if (input == null)
        {
            errors["Input"] = new[] { "Authentication settings are required." };
            input = new ClientAuthenticationInputModel();
        }

        var grantTypes = input.GrantTypes?
            .Where(grantType => !string.IsNullOrWhiteSpace(grantType))
            .Select(grantType => grantType.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? new();
        if (grantTypes.Count == 0)
        {
            errors["Input.GrantTypes"] = new[] { "At least one grant type must be selected." };
        }
        else if (grantTypes.Any(grantType => grantType.Length > ValidationConstants.MaxGrantTypeLength))
        {
            errors["Input.GrantTypes"] = new[] { $"Grant types cannot exceed {ValidationConstants.MaxGrantTypeLength} characters." };
        }

        var redirectUris = input.RedirectUris?
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? new();

        if (redirectUris.Any(uri => !UriValidationHelper.IsValidHttpOrHttpsUri(uri, ValidationConstants.MaxClientRedirectUriLength)))
        {
            errors["Input.RedirectUris"] = new[] { "Each Redirect URI must be an absolute HTTP or HTTPS URL within the configured length limit." };
        }

        var postLogoutUris = input.PostLogoutRedirectUris?
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? new();

        if (postLogoutUris.Any(uri => !UriValidationHelper.IsValidHttpOrHttpsUri(uri, ValidationConstants.MaxClientPostLogoutRedirectUriLength)))
        {
            errors["Input.PostLogoutRedirectUris"] = new[] { "Each Post-Logout Redirect URI must be an absolute HTTP or HTTPS URL within the configured length limit." };
        }

        var rawCorsOrigins = input.CorsOrigins?
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .ToList() ?? new();
        var corsOrigins = new List<string>();
        foreach (var origin in rawCorsOrigins)
        {
            if (!UriValidationHelper.TryNormalizeCorsOrigin(origin, ValidationConstants.MaxClientCorsOriginLength, out var normalizedOrigin))
            {
                errors["Input.CorsOrigins"] = new[] { "Each CORS Origin must contain only an HTTP or HTTPS scheme, host, and optional port." };
                break;
            }

            if (!corsOrigins.Contains(normalizedOrigin, StringComparer.OrdinalIgnoreCase))
            {
                corsOrigins.Add(normalizedOrigin);
            }
        }

        if (!string.IsNullOrWhiteSpace(input.FrontChannelLogoutUri) && !UriValidationHelper.IsValidHttpOrHttpsUri(input.FrontChannelLogoutUri, ValidationConstants.MaxLogoutUriLength))
        {
            errors["Input.FrontChannelLogoutUri"] = new[] { "Front-channel logout URI must be an absolute HTTP or HTTPS URL within the configured length limit." };
        }

        if (!string.IsNullOrWhiteSpace(input.BackChannelLogoutUri) && !UriValidationHelper.IsValidHttpOrHttpsUri(input.BackChannelLogoutUri, ValidationConstants.MaxLogoutUriLength))
        {
            errors["Input.BackChannelLogoutUri"] = new[] { "Back-channel logout URI must be an absolute HTTP or HTTPS URL within the configured length limit." };
        }

        if (errors.Count > 0)
        {
            await AuditDeniedAsync(AuditActions.UpdateAuthentication, AuditReasonCodes.ValidationFailed, clientId, clientId,
                "Client authentication validation failed.", cancellationToken);
            return AdminMutationResult.ValidationFailure(errors);
        }

        var outcome = AdminMutationResult.NotFoundResult();
        var targetName = clientId;
        try
        {
            var strategy = _configurationDbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _configurationDbContext.ChangeTracker.Clear();
                await using var transaction = await _configurationDbContext.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable, cancellationToken);
                var client = await LoadCompleteClientAsync(clientId, asNoTracking: false, cancellationToken);
                if (client == null)
                {
                    outcome = AdminMutationResult.NotFoundResult();
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                targetName = client.ClientName ?? clientId;
                var proposed = client.ToModel();
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
                var validationError = await ValidateClientAsync(proposed, cancellationToken);
                if (validationError != null)
                {
                    outcome = AdminMutationResult.ValidationFailure("Input.GrantTypes", validationError);
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                client.RequirePkce = input.RequirePkce;
                client.RequireClientSecret = input.RequireClientSecret;
                ReplaceCollection(client.AllowedGrantTypes, grantTypes, grantType => new ClientGrantType { GrantType = grantType });
                ReplaceCollection(client.RedirectUris, redirectUris, uri => new ClientRedirectUri { RedirectUri = uri });
                ReplaceCollection(client.PostLogoutRedirectUris, postLogoutUris, uri => new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = uri });
                ReplaceCollection(client.AllowedCorsOrigins, corsOrigins, origin => new ClientCorsOrigin { Origin = origin });
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
            await AuditFailedAsync(AuditActions.UpdateAuthentication, clientId, clientId, ex, cancellationToken);
            throw;
        }

        if (!outcome.Succeeded)
        {
            var reason = outcome.Status == AdminMutationStatus.NotFound
                ? AuditReasonCodes.NotFound
                : AuditReasonCodes.ValidationFailed;
            await AuditDeniedAsync(AuditActions.UpdateAuthentication, reason, clientId, targetName,
                outcome.ErrorMessage ?? "Client authentication validation failed.", cancellationToken);
            return outcome;
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, AuditActions.UpdateAuthentication, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
            TargetId: clientId, TargetName: targetName, Details: "Updated authentication settings"), cancellationToken);
        return outcome;
    }

    #endregion

    #region Permissions

    public async Task<ClientPermissionsModel?> GetClientPermissionsAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        var client = await _configurationDbContext.Clients
            .AsNoTracking()
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.AllowedScopes)
            .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);

        if (client == null)
        {
            return null;
        }

        var isInteractive = IsInteractiveClient(client);

        var identityScopes = await _configurationDbContext.IdentityResources
            .AsNoTracking()
            .Select(i => i.Name)
            .OrderBy(n => n)
            .ToListAsync(cancellationToken);

        var apiScopes = await _configurationDbContext.ApiScopes
            .AsNoTracking()
            .Select(a => a.Name)
            .OrderBy(n => n)
            .ToListAsync(cancellationToken);

        return new ClientPermissionsModel
        {
            ClientId = client.ClientId,
            ClientName = string.IsNullOrWhiteSpace(client.ClientName) ? client.ClientId : client.ClientName,
            IsInteractive = isInteractive,
            AllowedScopes = client.AllowedScopes.OrderBy(s => s.Id).Select(s => s.Scope).ToList(),
            AvailableIdentityScopes = isInteractive ? identityScopes : new List<string>(),
            AvailableApiScopes = apiScopes
        };
    }

    public Task<AdminMutationResult> UpdateClientPermissionsAsync(string clientId, List<string> allowedScopes, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.UpdatePermissions,
            clientId ?? string.Empty,
            clientId ?? string.Empty,
            () => UpdateClientPermissionsCoreAsync(clientId ?? string.Empty, allowedScopes ?? new List<string>(), cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> UpdateClientPermissionsCoreAsync(string clientId, List<string> allowedScopes, CancellationToken cancellationToken = default)
    {
        clientId = clientId?.Trim() ?? string.Empty;
        if (clientId.Length == 0)
        {
            await AuditDeniedAsync(AuditActions.UpdatePermissions, AuditReasonCodes.ValidationFailed, clientId, clientId,
                "Client permissions validation failed.", cancellationToken);
            return AdminMutationResult.ValidationFailure("Id", "Client ID is required.");
        }

        var requestedScopes = (allowedScopes ?? new List<string>())
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (requestedScopes.Any(scope => scope.Length > ValidationConstants.MaxScopeNameLength))
        {
            await AuditDeniedAsync(AuditActions.UpdatePermissions, AuditReasonCodes.ValidationFailed, clientId, clientId,
                "Client permissions validation failed.", cancellationToken);
            return AdminMutationResult.ValidationFailure(
                "Input.AllowedScopes",
                $"Scopes cannot exceed {ValidationConstants.MaxScopeNameLength} characters.");
        }

        var outcome = AdminMutationResult.NotFoundResult();
        var targetName = clientId;
        try
        {
            var strategy = _configurationDbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _configurationDbContext.ChangeTracker.Clear();
                await using var transaction = await _configurationDbContext.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable, cancellationToken);
                var client = await LoadCompleteClientAsync(clientId, asNoTracking: false, cancellationToken);
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

                var isInteractive = IsInteractiveClient(client);
                var finalScopes = isInteractive
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

                var proposed = client.ToModel();
                proposed.AllowedScopes = finalScopes;
                var validationError = await ValidateClientAsync(proposed, cancellationToken);
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
            await AuditFailedAsync(AuditActions.UpdatePermissions, clientId, clientId, ex, cancellationToken);
            throw;
        }

        if (!outcome.Succeeded)
        {
            var reason = outcome.Status == AdminMutationStatus.NotFound
                ? AuditReasonCodes.NotFound
                : AuditReasonCodes.ValidationFailed;
            await AuditDeniedAsync(AuditActions.UpdatePermissions, reason, clientId, targetName,
                outcome.ErrorMessage ?? "Client permissions validation failed.", cancellationToken);
            return outcome;
        }

        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, AuditActions.UpdatePermissions, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
            TargetId: clientId, TargetName: targetName, Details: "Updated allowed scopes"), cancellationToken);
        return outcome;
    }

    #endregion

    #region Secrets

    public async Task<ClientSecretsModel?> GetClientSecretsAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        var client = await _configurationDbContext.Clients
            .AsNoTracking()
            .Include(c => c.ClientSecrets)
            .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);

        if (client == null)
        {
            return null;
        }

        return new ClientSecretsModel
        {
            ClientId = client.ClientId,
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

    public Task<ClientSecretGenerateResult> GenerateClientSecretAsync(string clientId, string? description, DateTime? expiration = null, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.GenerateSecret,
            clientId ?? string.Empty,
            clientId ?? string.Empty,
            () => GenerateClientSecretCoreAsync(clientId ?? string.Empty, description, expiration, cancellationToken),
            cancellationToken);

    private async Task<ClientSecretGenerateResult> GenerateClientSecretCoreAsync(string clientId, string? description, DateTime? expiration = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            await AuditDeniedAsync(AuditActions.GenerateSecret, AuditReasonCodes.NotFound, clientId, clientId,
                "Client not found.", cancellationToken);
            return ClientSecretGenerateResult.Failed("Client not found.");
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription != null && trimmedDescription.Length > ValidationConstants.MaxClientSecretDescriptionLength)
        {
            var message = $"Secret description cannot exceed {ValidationConstants.MaxClientSecretDescriptionLength} characters.";
            await AuditDeniedAsync(AuditActions.GenerateSecret, AuditReasonCodes.ValidationFailed, clientId, clientId,
                message, cancellationToken);
            return ClientSecretGenerateResult.ValidationFailure("Description", message);
        }

        if (expiration.HasValue && expiration.Value.ToUniversalTime() <= DateTime.UtcNow)
        {
            var message = "Expiration date must be in the future.";
            await AuditDeniedAsync(AuditActions.GenerateSecret, AuditReasonCodes.ValidationFailed, clientId, clientId,
                message, cancellationToken);
            return ClientSecretGenerateResult.ValidationFailure("Expiration", message);
        }

        await using var transaction = await _configurationDbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            var client = await _configurationDbContext.Clients
                .Include(c => c.ClientSecrets)
                .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);

            if (client == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await AuditDeniedAsync(AuditActions.GenerateSecret, AuditReasonCodes.NotFound, clientId, clientId,
                    "Client not found.", cancellationToken);
                return ClientSecretGenerateResult.Failed("Client not found.");
            }

            var plaintextSecret = CryptoRandom.CreateUniqueId(32);
            client.ClientSecrets.Add(new ClientSecret
            {
                Description = trimmedDescription,
                Value = plaintextSecret.Sha256(),
                Type = SecretTypeShared,
                Expiration = expiration?.ToUniversalTime(),
                Created = DateTime.UtcNow
            });

            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.Client, AuditActions.GenerateSecret, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: clientId, TargetName: client.ClientName ?? clientId,
                Details: "Generated new client secret"), cancellationToken);
            return ClientSecretGenerateResult.Succeeded(plaintextSecret);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            await AuditFailedAsync(AuditActions.GenerateSecret, clientId, clientId, ex, cancellationToken);
            throw;
        }
    }

    public Task<ClientSecretRevokeResult> RevokeClientSecretAsync(string clientId, int secretId, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.RevokeSecret,
            clientId ?? string.Empty,
            clientId ?? string.Empty,
            () => RevokeClientSecretCoreAsync(clientId ?? string.Empty, secretId, cancellationToken),
            cancellationToken);

    private async Task<ClientSecretRevokeResult> RevokeClientSecretCoreAsync(string clientId, int secretId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            await AuditDeniedAsync(AuditActions.RevokeSecret, AuditReasonCodes.NotFound, clientId, clientId,
                "Client not found.", cancellationToken);
            return ClientSecretRevokeResult.Failed(
                "Client not found.", AuditReasonCodes.NotFound, AdminMutationStatus.NotFound);
        }

        var targetName = clientId;
        var outcome = ClientSecretRevokeResult.Failed(
            "Client not found.", AuditReasonCodes.NotFound, AdminMutationStatus.NotFound);
        var utcNow = _timeProvider.GetUtcNow();
        try
        {
            var strategy = _configurationDbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _configurationDbContext.ChangeTracker.Clear();
                await using var transaction = await _configurationDbContext.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable, cancellationToken);

                var client = await _configurationDbContext.Clients
                    .Include(c => c.ClientSecrets)
                    .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);

                if (client == null)
                {
                    outcome = ClientSecretRevokeResult.Failed(
                        "Client not found.", AuditReasonCodes.NotFound, AdminMutationStatus.NotFound);
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                targetName = client.ClientName ?? clientId;
                var secret = client.ClientSecrets.FirstOrDefault(s => s.Id == secretId);
                if (secret == null)
                {
                    outcome = ClientSecretRevokeResult.Failed(
                        "Secret not found.", AuditReasonCodes.NotFound, AdminMutationStatus.NotFound);
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                var hasUsableReplacement = client.ClientSecrets.Any(s =>
                    s.Id != secretId && (!s.Expiration.HasValue || s.Expiration.Value > utcNow.UtcDateTime));
                if (client.RequireClientSecret && !hasUsableReplacement)
                {
                    var message = "This is the last usable secret on a confidential client and cannot be revoked. Generate a usable replacement secret first, or disable the client's secret requirement.";
                    outcome = ClientSecretRevokeResult.Failed(message, AuditReasonCodes.LastUsableSecret);
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                client.ClientSecrets.Remove(secret);
                await _configurationDbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                outcome = ClientSecretRevokeResult.Succeeded();
            });

            if (!outcome.Success)
            {
                await AuditDeniedAsync(AuditActions.RevokeSecret, outcome.ReasonCode!, clientId, targetName,
                    outcome.ErrorMessage!, cancellationToken);
                return outcome;
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.Client, AuditActions.RevokeSecret, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: clientId, TargetName: targetName,
                Details: "Revoked client secret"), cancellationToken);

            return outcome;
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.RevokeSecret, clientId, targetName, ex, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Token settings

    public async Task<ClientTokenSettingsModel?> GetClientTokenSettingsAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        var client = await _configurationDbContext.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);

        if (client == null)
        {
            return null;
        }

        return new ClientTokenSettingsModel
        {
            ClientId = client.ClientId,
            ClientName = string.IsNullOrWhiteSpace(client.ClientName) ? client.ClientId : client.ClientName,
            AccessTokenLifetime = client.AccessTokenLifetime,
            IdentityTokenLifetime = client.IdentityTokenLifetime,
            RequireConsent = client.RequireConsent,
            AllowOfflineAccess = client.AllowOfflineAccess,
            RefreshTokenUsage = client.RefreshTokenUsage,
            RefreshTokenExpiration = client.RefreshTokenExpiration,
            AbsoluteRefreshTokenLifetime = client.AbsoluteRefreshTokenLifetime,
            SlidingRefreshTokenLifetime = client.SlidingRefreshTokenLifetime
        };
    }

    public Task<AdminMutationResult> UpdateClientTokenSettingsAsync(string clientId, ClientTokenSettingsInputModel input, CancellationToken cancellationToken = default) =>
        ExecuteAuditedAsync(
            AuditActions.UpdateTokenSettings,
            clientId ?? string.Empty,
            clientId ?? string.Empty,
            () => UpdateClientTokenSettingsCoreAsync(clientId ?? string.Empty, input, cancellationToken),
            cancellationToken);

    private async Task<AdminMutationResult> UpdateClientTokenSettingsCoreAsync(string clientId, ClientTokenSettingsInputModel input, CancellationToken cancellationToken = default)
    {
        clientId = clientId?.Trim() ?? string.Empty;
        var errors = new Dictionary<string, string[]>();
        if (clientId.Length == 0)
        {
            errors["Id"] = new[] { "Client ID is required." };
        }
        if (input == null)
        {
            errors["Input"] = new[] { "Token settings are required." };
        }
        else
        {
            if (input.AccessTokenLifetime < ValidationConstants.MinAccessTokenLifetime
                || input.AccessTokenLifetime > ValidationConstants.MaxAccessTokenLifetime)
            {
                errors["Input.AccessTokenLifetime"] = new[]
                {
                    $"Access Token Lifetime must be between {ValidationConstants.MinAccessTokenLifetime} and {ValidationConstants.MaxAccessTokenLifetime} seconds."
                };
            }
            if (input.IdentityTokenLifetime < ValidationConstants.MinIdentityTokenLifetime
                || input.IdentityTokenLifetime > ValidationConstants.MaxIdentityTokenLifetime)
            {
                errors["Input.IdentityTokenLifetime"] = new[]
                {
                    $"Identity Token Lifetime must be between {ValidationConstants.MinIdentityTokenLifetime} and {ValidationConstants.MaxIdentityTokenLifetime} seconds."
                };
            }
            
            if (input.AllowOfflineAccess)
            {
                if (input.AbsoluteRefreshTokenLifetime < ValidationConstants.MinRefreshTokenLifetime
                    || input.AbsoluteRefreshTokenLifetime > ValidationConstants.MaxAbsoluteRefreshTokenLifetime)
                {
                    errors["Input.AbsoluteRefreshTokenLifetime"] = new[]
                    {
                        $"Absolute Refresh Token Lifetime must be between {ValidationConstants.MinRefreshTokenLifetime} and {ValidationConstants.MaxAbsoluteRefreshTokenLifetime} seconds."
                    };
                }
                
                if (input.SlidingRefreshTokenLifetime < ValidationConstants.MinRefreshTokenLifetime
                    || input.SlidingRefreshTokenLifetime > ValidationConstants.MaxSlidingRefreshTokenLifetime)
                {
                    errors["Input.SlidingRefreshTokenLifetime"] = new[]
                    {
                        $"Sliding Refresh Token Lifetime must be between {ValidationConstants.MinRefreshTokenLifetime} and {ValidationConstants.MaxSlidingRefreshTokenLifetime} seconds."
                    };
                }

                if (input.SlidingRefreshTokenLifetime > input.AbsoluteRefreshTokenLifetime)
                {
                    errors["Input.SlidingRefreshTokenLifetime"] = new[]
                    {
                        "Sliding Refresh Token Lifetime cannot exceed the Absolute Refresh Token Lifetime."
                    };
                }
            }
        }
        if (errors.Count > 0)
        {
            await AuditDeniedAsync(AuditActions.UpdateTokenSettings, AuditReasonCodes.ValidationFailed, clientId, clientId,
                "Client token settings validation failed.", cancellationToken);
            return AdminMutationResult.ValidationFailure(errors);
        }

        var client = await LoadCompleteClientAsync(clientId, asNoTracking: true, cancellationToken);

        if (client == null)
        {
            await AuditDeniedAsync(AuditActions.UpdateTokenSettings, AuditReasonCodes.NotFound, clientId, clientId,
                $"Client '{clientId}' was not found.", cancellationToken);
            return AdminMutationResult.NotFoundResult();
        }

        try
        {
            var proposed = client.ToModel();
            proposed.AccessTokenLifetime = input!.AccessTokenLifetime;
            proposed.IdentityTokenLifetime = input.IdentityTokenLifetime;
            proposed.RequireConsent = input.RequireConsent;
            proposed.AllowOfflineAccess = input.AllowOfflineAccess;
            if (input.AllowOfflineAccess)
            {
                proposed.RefreshTokenUsage = (Duende.IdentityServer.Models.TokenUsage)input.RefreshTokenUsage;
                proposed.RefreshTokenExpiration = (Duende.IdentityServer.Models.TokenExpiration)input.RefreshTokenExpiration;
                proposed.AbsoluteRefreshTokenLifetime = input.AbsoluteRefreshTokenLifetime;
                proposed.SlidingRefreshTokenLifetime = input.SlidingRefreshTokenLifetime;
            }
            var validationError = await ValidateClientAsync(proposed, cancellationToken);
            if (validationError != null)
            {
                await AuditDeniedAsync(AuditActions.UpdateTokenSettings, AuditReasonCodes.ValidationFailed, clientId,
                    client.ClientName ?? clientId, "Client token settings validation failed.", cancellationToken);
                return AdminMutationResult.ValidationFailure("Input.AccessTokenLifetime", validationError);
            }

            var trackedClient = await _configurationDbContext.Clients
                .FirstAsync(c => c.ClientId == clientId, cancellationToken);
            var oldValues = new ClientTokenSettingsAuditValue(
                trackedClient.AccessTokenLifetime,
                trackedClient.IdentityTokenLifetime,
                trackedClient.RequireConsent,
                trackedClient.AllowOfflineAccess,
                trackedClient.RefreshTokenUsage,
                trackedClient.RefreshTokenExpiration,
                trackedClient.AbsoluteRefreshTokenLifetime,
                trackedClient.SlidingRefreshTokenLifetime);
            trackedClient.AccessTokenLifetime = input.AccessTokenLifetime;
            trackedClient.IdentityTokenLifetime = input.IdentityTokenLifetime;
            trackedClient.RequireConsent = input.RequireConsent;
            trackedClient.AllowOfflineAccess = input.AllowOfflineAccess;
            if (input.AllowOfflineAccess)
            {
                trackedClient.RefreshTokenUsage = input.RefreshTokenUsage;
                trackedClient.RefreshTokenExpiration = input.RefreshTokenExpiration;
                trackedClient.AbsoluteRefreshTokenLifetime = input.AbsoluteRefreshTokenLifetime;
                trackedClient.SlidingRefreshTokenLifetime = input.SlidingRefreshTokenLifetime;
            }

            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.Client, AuditActions.UpdateTokenSettings, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: clientId, TargetName: client.ClientName ?? clientId,
                OldValues: oldValues,
                NewValues: new ClientTokenSettingsAuditValue(
                    trackedClient.AccessTokenLifetime,
                    trackedClient.IdentityTokenLifetime,
                    trackedClient.RequireConsent,
                    trackedClient.AllowOfflineAccess,
                    trackedClient.RefreshTokenUsage,
                    trackedClient.RefreshTokenExpiration,
                    trackedClient.AbsoluteRefreshTokenLifetime,
                    trackedClient.SlidingRefreshTokenLifetime
                ),
                Details: "Updated token and consent settings"), cancellationToken);

            return AdminMutationResult.Success();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.UpdateTokenSettings, clientId, client.ClientName ?? clientId, ex, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Shared infrastructure

    private async Task<Client?> LoadCompleteClientAsync(
        string clientId,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var query = _configurationDbContext.Clients
            .AsSplitQuery()
            .Include(client => client.AllowedGrantTypes)
            .Include(client => client.RedirectUris)
            .Include(client => client.PostLogoutRedirectUris)
            .Include(client => client.AllowedCorsOrigins)
            .Include(client => client.AllowedScopes)
            .Include(client => client.ClientSecrets)
            .Include(client => client.Claims)
            .Include(client => client.IdentityProviderRestrictions)
            .Include(client => client.Properties)
            .AsQueryable();

        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(client => client.ClientId == clientId, cancellationToken);
    }

    private async Task<string?> ValidateClientAsync(
        Duende.IdentityServer.Models.Client proposed,
        CancellationToken cancellationToken)
    {
        var validationContext = new ClientConfigurationValidationContext(proposed);
        await _clientConfigurationValidator.ValidateAsync(validationContext, cancellationToken);
        return validationContext.IsValid
            ? null
            : validationContext.ErrorMessage ?? "Invalid client configuration.";
    }

    private static void ReplaceCollection<TValue, TEntity>(
        ICollection<TEntity> target,
        IEnumerable<TValue> values,
        Func<TValue, TEntity> create)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(create(value));
        }
    }

    private Task AuditDeniedAsync(string action, string reasonCode, string targetId, string targetName, string details, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, action, AuditOutcome.Denied, reasonCode,
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
        const string marker = "IdentityServerProject.Audit.Client.Failed";
        if (ex.Data.Contains(marker))
        {
            return;
        }

        ex.Data[marker] = true;
        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, action, AuditOutcome.Failed, AuditReasonCodes.PersistenceFailure,
            TargetId: targetId, TargetName: targetName, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);

    #endregion
}
}
