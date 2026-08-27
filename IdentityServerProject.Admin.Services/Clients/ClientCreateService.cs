using System;
using System.Collections.Generic;
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
using IdentityServerProject.Services.Secrets;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using static Duende.IdentityServer.Models.HashExtensions;
using Client = Duende.IdentityServer.EntityFramework.Entities.Client;

namespace IdentityServerProject.Services.Clients;

public partial class ClientCreateService : IClientCreateService
{
    public const string PresetPropertyKey = "admin:preset";

    private readonly ConfigurationDbContext _configurationDbContext;
    private readonly IAuditWriter _auditWriter;
    private readonly IClientConfigurationValidator _clientConfigurationValidator;

    public ClientCreateService(
        ConfigurationDbContext configurationDbContext,
        IAuditWriter auditWriter,
        IClientConfigurationValidator clientConfigurationValidator)
    {
        _configurationDbContext = configurationDbContext;
        _auditWriter = auditWriter;
        _clientConfigurationValidator = clientConfigurationValidator;
    }

    #region Scope discovery

    public async Task<List<string>> GetAvailableScopesAsync(CancellationToken cancellationToken = default)
    {
        var identityScopes = await _configurationDbContext.IdentityResources
            .AsNoTracking()
            .Select(i => i.Name)
            .ToListAsync(cancellationToken);

        var apiScopes = await _configurationDbContext.ApiScopes
            .AsNoTracking()
            .Select(a => a.Name)
            .ToListAsync(cancellationToken);

        var availableScopes = identityScopes
            .Concat(apiScopes)
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        return availableScopes;
    }

    #endregion

    #region Client creation and cloning

    public Task<ClientCreateResult> CreateClientAsync(ClientCreateInputModel input, CancellationToken cancellationToken = default)
    {
        var targetId = input?.ClientId?.Trim() ?? string.Empty;
        var targetName = input?.ClientName?.Trim() ?? targetId;
        return ExecuteAuditedAsync(
            targetId,
            targetName,
            () => CreateClientCoreAsync(input!, cancellationToken),
            cancellationToken);
    }

    public Task<ClientCreateResult> CloneClientAsync(string sourceClientId, ClientCreateInputModel input, CancellationToken cancellationToken = default)
    {
        var targetId = input?.ClientId?.Trim() ?? string.Empty;
        var targetName = input?.ClientName?.Trim() ?? targetId;
        return ExecuteAuditedAsync(
            targetId,
            targetName,
            () => CloneClientCoreAsync(sourceClientId, input!, cancellationToken),
            cancellationToken);
    }

    private async Task<ClientCreateResult> CloneClientCoreAsync(string sourceClientId, ClientCreateInputModel input, CancellationToken cancellationToken = default)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        var sourceClient = await _configurationDbContext.Clients
            .AsNoTracking()
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.RedirectUris)
            .Include(c => c.PostLogoutRedirectUris)
            .Include(c => c.AllowedCorsOrigins)
            .Include(c => c.AllowedScopes)
            .Include(c => c.Properties)
            .FirstOrDefaultAsync(c => c.ClientId == sourceClientId, cancellationToken);

        if (sourceClient == null)
        {
            await AuditDeniedAsync(AuditReasonCode.NotFound, input.ClientId ?? string.Empty, input.ClientName ?? string.Empty,
                $"Source client '{sourceClientId}' was not found.", cancellationToken);
            return ClientCreateResult.Failed("Source client not found.");
        }

        var clientId = input.ClientId?.Trim();
        var clientName = input.ClientName?.Trim();
        var description = input.Description?.Trim();

        var clientIdIsInUse = await _configurationDbContext.Clients
            .AsNoTracking()
            .AnyAsync(c => c.ClientId == clientId, cancellationToken);

        if (clientIdIsInUse)
        {
            await AuditDeniedAsync(AuditReasonCode.NameCollision, clientId!, clientName!,
                $"A client with ID '{clientId}' already exists.", cancellationToken);
            return ClientCreateResult.Failed("ClientId", $"A client with ID '{clientId}' already exists.", AdminMutationStatus.Conflict);
        }

        var clonedClient = new Client
        {
            ClientId = clientId!,
            ClientName = clientName!,
            Description = description,
            Enabled = sourceClient.Enabled,
            ProtocolType = sourceClient.ProtocolType,
            RequireClientSecret = sourceClient.RequireClientSecret,
            RequireConsent = sourceClient.RequireConsent,
            AllowRememberConsent = sourceClient.AllowRememberConsent,
            AlwaysIncludeUserClaimsInIdToken = sourceClient.AlwaysIncludeUserClaimsInIdToken,
            RequirePkce = sourceClient.RequirePkce,
            AllowPlainTextPkce = sourceClient.AllowPlainTextPkce,
            RequireRequestObject = sourceClient.RequireRequestObject,
            AllowAccessTokensViaBrowser = sourceClient.AllowAccessTokensViaBrowser,
            FrontChannelLogoutUri = sourceClient.FrontChannelLogoutUri,
            FrontChannelLogoutSessionRequired = sourceClient.FrontChannelLogoutSessionRequired,
            BackChannelLogoutUri = sourceClient.BackChannelLogoutUri,
            BackChannelLogoutSessionRequired = sourceClient.BackChannelLogoutSessionRequired,
            AllowOfflineAccess = sourceClient.AllowOfflineAccess,
            IdentityTokenLifetime = sourceClient.IdentityTokenLifetime,
            AllowedIdentityTokenSigningAlgorithms = sourceClient.AllowedIdentityTokenSigningAlgorithms,
            AccessTokenLifetime = sourceClient.AccessTokenLifetime,
            AuthorizationCodeLifetime = sourceClient.AuthorizationCodeLifetime,
            ConsentLifetime = sourceClient.ConsentLifetime,
            AbsoluteRefreshTokenLifetime = sourceClient.AbsoluteRefreshTokenLifetime,
            SlidingRefreshTokenLifetime = sourceClient.SlidingRefreshTokenLifetime,
            RefreshTokenUsage = sourceClient.RefreshTokenUsage,
            UpdateAccessTokenClaimsOnRefresh = sourceClient.UpdateAccessTokenClaimsOnRefresh,
            RefreshTokenExpiration = sourceClient.RefreshTokenExpiration,
            AccessTokenType = sourceClient.AccessTokenType,
            EnableLocalLogin = sourceClient.EnableLocalLogin,
            IncludeJwtId = sourceClient.IncludeJwtId,
            AlwaysSendClientClaims = sourceClient.AlwaysSendClientClaims,
            ClientClaimsPrefix = sourceClient.ClientClaimsPrefix,
            PairWiseSubjectSalt = sourceClient.PairWiseSubjectSalt,
            UserSsoLifetime = sourceClient.UserSsoLifetime,
            UserCodeType = sourceClient.UserCodeType,
            DeviceCodeLifetime = sourceClient.DeviceCodeLifetime,
            CibaLifetime = sourceClient.CibaLifetime,
            PollingInterval = sourceClient.PollingInterval,
            CoordinateLifetimeWithUserSession = sourceClient.CoordinateLifetimeWithUserSession,

            AllowedGrantTypes = sourceClient.AllowedGrantTypes.Select(g => new ClientGrantType { GrantType = g.GrantType }).ToList(),
            RedirectUris = sourceClient.RedirectUris.Select(u => new ClientRedirectUri { RedirectUri = u.RedirectUri }).ToList(),
            PostLogoutRedirectUris = sourceClient.PostLogoutRedirectUris.Select(u => new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = u.PostLogoutRedirectUri }).ToList(),
            AllowedCorsOrigins = sourceClient.AllowedCorsOrigins.Select(c => new ClientCorsOrigin { Origin = c.Origin }).ToList(),
            AllowedScopes = sourceClient.AllowedScopes.Select(s => new ClientScope { Scope = s.Scope }).ToList(),
            Properties = sourceClient.Properties.Select(p => new ClientProperty { Key = p.Key, Value = p.Value }).ToList(),
            ClientSecrets = new List<ClientSecret>()
        };

        string? plaintextSecret = null;
        if (clonedClient.RequireClientSecret)
        {
            plaintextSecret = CryptoRandom.CreateUniqueId(32);
            clonedClient.ClientSecrets.Add(new ClientSecret
            {
                Description = "Initial client secret (cloned)",
                Value = plaintextSecret.Sha256(),
                Type = SecretType.SharedSecret.ToSecretTypeValue(),
                Created = DateTime.UtcNow
            });
        }

        var clientModel = clonedClient.ToModel();
        var validationContext = new ClientConfigurationValidationContext(clientModel);
        await _clientConfigurationValidator.ValidateAsync(validationContext, cancellationToken);

        if (!validationContext.IsValid)
        {
            await AuditDeniedAsync(AuditReasonCode.ValidationFailed, clientId!, clientName!,
                validationContext.ErrorMessage ?? "Invalid client configuration.", cancellationToken);
            return ClientCreateResult.Failed(validationContext.ErrorMessage ?? "Invalid client configuration.");
        }

        try
        {
            _configurationDbContext.Clients.Add(clonedClient);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.Client, AuditAction.Create, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: clientId, TargetName: clientName,
                Details: $"Cloned client '{clientName}' from '{sourceClientId}'"), cancellationToken);

            return ClientCreateResult.Succeeded(clientId!, plaintextSecret);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await AuditDeniedAsync(AuditReasonCode.NameCollision, clientId!, clientName!,
                $"A client with ID '{clientId}' already exists.", cancellationToken);
            return ClientCreateResult.Failed("ClientId", $"A client with ID '{clientId}' already exists.", AdminMutationStatus.Conflict);
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(clientId!, clientName!, ex, cancellationToken);
            throw;
        }
    }

    private async Task<ClientCreateResult> CreateClientCoreAsync(ClientCreateInputModel input, CancellationToken cancellationToken = default)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        var errors = new ValidationErrorDictionary();

        var clientId = input.ClientId?.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            errors.AddError("ClientId", "Client ID is required.");
        }
        else if (clientId.Length > ValidationConstants.MaxClientIdLength)
        {
            errors.AddError("ClientId", $"Client ID cannot exceed {ValidationConstants.MaxClientIdLength} characters.");
        }

        var clientName = input.ClientName?.Trim();
        if (string.IsNullOrWhiteSpace(clientName))
        {
            errors.AddError("ClientName", "Client Name is required.");
        }
        else if (clientName.Length > ValidationConstants.MaxNameLength)
        {
            errors.AddError("ClientName", $"Client Name cannot exceed {ValidationConstants.MaxNameLength} characters.");
        }

        var description = input.Description?.Trim();
        if (description != null && description.Length > ValidationConstants.MaxDescriptionLength)
        {
            errors.AddError("Description", $"Description cannot exceed {ValidationConstants.MaxDescriptionLength} characters.");
        }

        var redirectUris = input.RedirectUris?
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct()
            .ToList() ?? new();

        foreach (var uri in redirectUris)
        {
            if (!UriValidationHelper.IsValidHttpOrHttpsUri(uri, ValidationConstants.MaxClientRedirectUriLength))
            {
                errors.AddError("RedirectUris", $"Redirect URI '{uri}' must be an absolute HTTP or HTTPS URL.");
                break;
            }
        }

        var postLogoutUris = input.PostLogoutRedirectUris?
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct()
            .ToList() ?? new();

        foreach (var uri in postLogoutUris)
        {
            if (!UriValidationHelper.IsValidHttpOrHttpsUri(uri, ValidationConstants.MaxClientPostLogoutRedirectUriLength))
            {
                errors.AddError("PostLogoutRedirectUris", $"Post-logout redirect URI '{uri}' must be an absolute HTTP or HTTPS URL.");
                break;
            }
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
                errors.AddError("CorsOrigins", $"CORS origin '{origin}' must contain only an HTTP or HTTPS scheme, host, and optional port.");
                break;
            }

            if (!corsOrigins.Contains(normalizedOrigin, StringComparer.OrdinalIgnoreCase))
            {
                corsOrigins.Add(normalizedOrigin);
            }
        }

        var grantTypes = input.GrantTypes?
            .Where(grantType => !string.IsNullOrWhiteSpace(grantType))
            .Select(grantType => grantType.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? new();
        if (grantTypes.Any(grantType => grantType.Length > ValidationConstants.MaxGrantTypeLength))
        {
            errors.AddError("GrantTypes", $"Grant types cannot exceed {ValidationConstants.MaxGrantTypeLength} characters.");
        }

        // Validate scopes against the durable resource sets. Never invent fallback scopes:
        // a name absent from both stores must be rejected rather than becoming active later.
        var validSystemScopes = new HashSet<string>(await GetAvailableScopesAsync(cancellationToken), StringComparer.Ordinal);

        var scopes = input.AllowedScopes?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? new();

        if (scopes.Any(scope => scope.Length > ValidationConstants.MaxScopeNameLength))
        {
            errors.AddError("AllowedScopes", $"Scopes cannot exceed {ValidationConstants.MaxScopeNameLength} characters.");
        }
        else
        {
            var unknownScope = scopes.FirstOrDefault(scope => !validSystemScopes.Contains(scope));
            if (unknownScope != null)
            {
                errors.AddError("AllowedScopes", $"Scope '{unknownScope}' is not a valid API scope or identity resource.");
            }
        }

        if (errors.HasErrors)
        {
            await AuditDeniedAsync(AuditReasonCode.ValidationFailed, clientId ?? string.Empty, clientName ?? string.Empty,
                "Client creation validation failed.", cancellationToken);
            return ClientCreateResult.Failed(errors);
        }

        var clientIdIsInUse = await _configurationDbContext.Clients
            .AsNoTracking()
            .AnyAsync(c => c.ClientId == clientId, cancellationToken);

        if (clientIdIsInUse)
        {
            await AuditDeniedAsync(AuditReasonCode.NameCollision, clientId!, clientName!,
                $"A client with ID '{clientId}' already exists.", cancellationToken);
            return ClientCreateResult.Failed("ClientId", $"A client with ID '{clientId}' already exists.", AdminMutationStatus.Conflict);
        }

        var client = new Client
        {
            ClientId = clientId!,
            ClientName = clientName!,
            Description = description,
            Enabled = true,
            RequirePkce = input.RequirePkce,
            RequireClientSecret = input.RequireClientSecret,
            AccessTokenLifetime = 3600,
            IdentityTokenLifetime = 300,
            AllowedGrantTypes = new List<ClientGrantType>(),
            RedirectUris = new List<ClientRedirectUri>(),
            PostLogoutRedirectUris = new List<ClientPostLogoutRedirectUri>(),
            AllowedCorsOrigins = new List<ClientCorsOrigin>(),
            AllowedScopes = new List<ClientScope>(),
            ClientSecrets = new List<ClientSecret>(),
            Properties = new List<ClientProperty>()
        };

        if (!string.IsNullOrWhiteSpace(input.SelectedPreset))
        {
            client.Properties.Add(new ClientProperty
            {
                Key = PresetPropertyKey,
                Value = input.SelectedPreset
            });
        }

        if (grantTypes.Count == 0)
        {
            grantTypes = input.SelectedPreset == "m2m"
                ? new() { "client_credentials" }
                : new() { "authorization_code" };
        }

        foreach (var grantType in grantTypes)
        {
            client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = grantType });
        }

        foreach (var uri in redirectUris)
        {
            client.RedirectUris.Add(new ClientRedirectUri { RedirectUri = uri });
        }

        foreach (var uri in postLogoutUris)
        {
            client.PostLogoutRedirectUris.Add(new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = uri });
        }

        foreach (var origin in corsOrigins)
        {
            client.AllowedCorsOrigins.Add(new ClientCorsOrigin { Origin = origin });
        }

        if (scopes.Count == 0 && input.SelectedPreset != "m2m")
        {
            if (!validSystemScopes.Contains("openid"))
            {
                await AuditDeniedAsync(AuditReasonCode.ValidationFailed, clientId!, clientName!,
                    "Client creation requires the 'openid' identity resource.", cancellationToken);
                return ClientCreateResult.Failed(
                    "AllowedScopes",
                    "The required 'openid' identity resource is not configured.");
            }

            scopes.Add("openid");
            if (validSystemScopes.Contains("profile"))
            {
                scopes.Add("profile");
            }
        }

        foreach (var scope in scopes)
        {
            client.AllowedScopes.Add(new ClientScope { Scope = scope });
        }

        string? plaintextSecret = null;
        if (input.RequireClientSecret)
        {
            plaintextSecret = CryptoRandom.CreateUniqueId(32);
            client.ClientSecrets.Add(new ClientSecret
            {
                Description = "Initial client secret",
                Value = plaintextSecret.Sha256(),
                Type = SecretType.SharedSecret.ToSecretTypeValue(),
                Created = DateTime.UtcNow
            });
        }

        var clientModel = client.ToModel();
        var validationContext = new ClientConfigurationValidationContext(clientModel);
        await _clientConfigurationValidator.ValidateAsync(validationContext, cancellationToken);

        if (!validationContext.IsValid)
        {
            await AuditDeniedAsync(AuditReasonCode.ValidationFailed, clientId!, clientName!,
                validationContext.ErrorMessage ?? "Invalid client configuration.", cancellationToken);
            return ClientCreateResult.Failed(validationContext.ErrorMessage ?? "Invalid client configuration.");
        }

        try
        {
            _configurationDbContext.Clients.Add(client);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.Client, AuditAction.Create, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: clientId, TargetName: clientName,
                Details: $"Created client '{clientName}'"), cancellationToken);

            return ClientCreateResult.Succeeded(clientId!, plaintextSecret);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await AuditDeniedAsync(AuditReasonCode.NameCollision, clientId!, clientName!,
                $"A client with ID '{clientId}' already exists.", cancellationToken);
            return ClientCreateResult.Failed("ClientId", $"A client with ID '{clientId}' already exists.", AdminMutationStatus.Conflict);
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(clientId!, clientName!, ex, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Auditing

    private async Task<ClientCreateResult> ExecuteAuditedAsync(
        string targetId,
        string targetName,
        Func<Task<ClientCreateResult>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(targetId, targetName, ex, cancellationToken);
            throw;
        }
    }

    private async Task AuditFailedAsync(
        string targetId,
        string targetName,
        Exception ex,
        CancellationToken cancellationToken)
    {
        const string marker = "IdentityServerProject.Audit.ClientCreate.Failed";
        if (ex.Data.Contains(marker))
            return;

        ex.Data[marker] = true;
        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.Client, AuditAction.Create, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
            TargetId: targetId, TargetName: targetName,
            Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
    }

    private Task AuditDeniedAsync(AuditReasonCode reasonCode, string targetId, string targetName, string details, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.Client, AuditAction.Create, AuditOutcome.Denied, reasonCode,
            TargetId: targetId, TargetName: targetName, Details: details), cancellationToken);

    #endregion
}
