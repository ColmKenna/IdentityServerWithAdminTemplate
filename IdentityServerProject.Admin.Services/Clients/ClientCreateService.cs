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
using Client = Duende.IdentityServer.EntityFramework.Entities.Client;

namespace IdentityServerProject.Services.Clients;

public partial class ClientCreateService(
    ConfigurationDbContext configurationDbContext,
    IAuditWriter auditWriter,
    IClientConfigurationValidator clientConfigurationValidator) : IClientCreateService
{
    public const string PresetPropertyKey = "admin:preset";
    private readonly IAuditWriter _auditWriter = auditWriter;
    private readonly IClientConfigurationValidator _clientConfigurationValidator = clientConfigurationValidator;

    private readonly ConfigurationDbContext _configurationDbContext = configurationDbContext;

    public async Task<List<string>> GetAvailableScopesAsync(CancellationToken cancellationToken = default)
    {
        List<string> identityScopes = await _configurationDbContext.IdentityResources
            .AsNoTracking()
            .Select(i => i.Name)
            .ToListAsync(cancellationToken);

        List<string> apiScopes = await _configurationDbContext.ApiScopes
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

    public Task<ClientCreateResult> CreateClientAsync(ClientCreateInputModel input,
        CancellationToken cancellationToken = default)
    {
        string targetId = input?.ClientId?.Trim() ?? string.Empty;
        string targetName = input?.ClientName?.Trim() ?? targetId;
        return ExecuteAuditedAsync(
            targetId,
            targetName,
            () => CreateClientCoreAsync(input!, cancellationToken),
            cancellationToken);
    }

    public Task<ClientCreateResult> CloneClientAsync(string sourceClientId, ClientCreateInputModel input,
        CancellationToken cancellationToken = default)
    {
        string targetId = input?.ClientId?.Trim() ?? string.Empty;
        string targetName = input?.ClientName?.Trim() ?? targetId;
        return ExecuteAuditedAsync(
            targetId,
            targetName,
            () => CloneClientCoreAsync(sourceClientId, input!, cancellationToken),
            cancellationToken);
    }

    private async Task<ClientCreateResult> CloneClientCoreAsync(string sourceClientId, ClientCreateInputModel input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
            throw new ArgumentNullException(nameof(input));

        Client? sourceClient = await _configurationDbContext.Clients
            .AsNoTracking()
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.RedirectUris)
            .Include(c => c.PostLogoutRedirectUris)
            .Include(c => c.AllowedCorsOrigins)
            .Include(c => c.AllowedScopes)
            .Include(c => c.Properties)
            .FirstOrDefaultAsync(c => c.ClientId == sourceClientId, cancellationToken);

        if (sourceClient is null)
            return await DenySourceClientNotFoundAsync(sourceClientId, input.ClientId, input.ClientName, cancellationToken);

        string? clientId = input.ClientId?.Trim();
        string? clientName = input.ClientName?.Trim();
        string? description = input.Description?.Trim();

        bool clientIdIsInUse = await _configurationDbContext.Clients
            .AsNoTracking()
            .AnyAsync(c => c.ClientId == clientId, cancellationToken);

        if (clientIdIsInUse)
            return await DenyClientIdInUseAsync(clientId!, clientName!, cancellationToken);

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

            AllowedGrantTypes = sourceClient.AllowedGrantTypes
                .Select(g => new ClientGrantType { GrantType = g.GrantType }).ToList(),
            RedirectUris = sourceClient.RedirectUris.Select(u => new ClientRedirectUri { RedirectUri = u.RedirectUri })
                .ToList(),
            PostLogoutRedirectUris = sourceClient.PostLogoutRedirectUris.Select(u => new ClientPostLogoutRedirectUri
            { PostLogoutRedirectUri = u.PostLogoutRedirectUri }).ToList(),
            AllowedCorsOrigins = sourceClient.AllowedCorsOrigins.Select(c => new ClientCorsOrigin { Origin = c.Origin })
                .ToList(),
            AllowedScopes = sourceClient.AllowedScopes.Select(s => new ClientScope { Scope = s.Scope }).ToList(),
            Properties = sourceClient.Properties.Select(p => new ClientProperty { Key = p.Key, Value = p.Value })
                .ToList(),
            ClientSecrets = []
        };

        string? plaintextSecret = null;
        if (clonedClient.RequireClientSecret)
        {
            plaintextSecret = CryptoRandom.CreateUniqueId();
            clonedClient.ClientSecrets.Add(new ClientSecret
            {
                Description = "Initial client secret (cloned)",
                Value = plaintextSecret.Sha256(),
                Type = SecretType.SharedSecret.ToSecretTypeValue(),
                Created = DateTime.UtcNow
            });
        }

        Duende.IdentityServer.Models.Client clientModel = clonedClient.ToModel();
        var validationContext = new ClientConfigurationValidationContext(clientModel);
        await _clientConfigurationValidator.ValidateAsync(validationContext, cancellationToken);

        if (!validationContext.IsValid)
            return await DenyInvalidClientConfigurationAsync(clientId!, clientName!, validationContext.ErrorMessage, cancellationToken);

        try
        {
            _configurationDbContext.Clients.Add(clonedClient);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.Client, AuditAction.Create, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                clientId, clientName,
                Details: $"Cloned client '{clientName}' from '{sourceClientId}'"), cancellationToken);

            return ClientCreateResult.Succeeded(clientId!, plaintextSecret);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await AuditDeniedAsync(AuditReasonCode.NameCollision, clientId!, clientName!,
                $"A client with ID '{clientId}' already exists.", cancellationToken);
            return ClientCreateResult.Failed("ClientId", $"A client with ID '{clientId}' already exists.",
                AdminMutationStatus.Conflict);
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(clientId!, clientName!, ex, cancellationToken);
            throw;
        }
    }

    private async Task<ClientCreateResult> CreateClientCoreAsync(ClientCreateInputModel input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
            throw new ArgumentNullException(nameof(input));

        var errors = new ValidationErrorDictionary();

        string? clientId = input.ClientId?.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
            errors.AddError("ClientId", "Client ID is required.");
        else if (clientId.Length > ValidationConstants.MaxClientIdLength)
            errors.AddError("ClientId", $"Client ID cannot exceed {ValidationConstants.MaxClientIdLength} characters.");

        string? clientName = input.ClientName?.Trim();
        if (string.IsNullOrWhiteSpace(clientName))
            errors.AddError("ClientName", "Client Name is required.");
        else if (clientName.Length > ValidationConstants.MaxNameLength)
            errors.AddError("ClientName", $"Client Name cannot exceed {ValidationConstants.MaxNameLength} characters.");

        string? description = input.Description?.Trim();
        if (description is not null && description.Length > ValidationConstants.MaxDescriptionLength)
            errors.AddError("Description",
                $"Description cannot exceed {ValidationConstants.MaxDescriptionLength} characters.");

        List<string> redirectUris = input.RedirectUris?
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct()
            .ToList() ?? [];

        foreach (string uri in redirectUris)
            if (!UriValidationHelper.IsValidHttpOrHttpsUri(uri, ValidationConstants.MaxClientRedirectUriLength))
            {
                errors.AddError("RedirectUris", $"Redirect URI '{uri}' must be an absolute HTTP or HTTPS URL.");
                break;
            }

        List<string> postLogoutUris = input.PostLogoutRedirectUris?
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct()
            .ToList() ?? [];

        foreach (string uri in postLogoutUris)
            if (!UriValidationHelper.IsValidHttpOrHttpsUri(uri,
                    ValidationConstants.MaxClientPostLogoutRedirectUriLength))
            {
                errors.AddError("PostLogoutRedirectUris",
                    $"Post-logout redirect URI '{uri}' must be an absolute HTTP or HTTPS URL.");
                break;
            }

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
                errors.AddError("CorsOrigins",
                    $"CORS origin '{origin}' must contain only an HTTP or HTTPS scheme, host, and optional port.");
                break;
            }

            if (!corsOrigins.Contains(normalizedOrigin, StringComparer.OrdinalIgnoreCase))
                corsOrigins.Add(normalizedOrigin);
        }

        List<string> grantTypes = input.GrantTypes?
            .Where(grantType => !string.IsNullOrWhiteSpace(grantType))
            .Select(grantType => grantType.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];
        if (grantTypes.Any(grantType => grantType.Length > ValidationConstants.MaxGrantTypeLength))
            errors.AddError("GrantTypes",
                $"Grant types cannot exceed {ValidationConstants.MaxGrantTypeLength} characters.");

        // Validate scopes against the durable resource sets. Never invent fallback scopes:
        // a name absent from both stores must be rejected rather than becoming active later.
        var validSystemScopes =
            new HashSet<string>(await GetAvailableScopesAsync(cancellationToken), StringComparer.Ordinal);

        List<string> scopes = input.AllowedScopes?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

        if (scopes.Any(scope => scope.Length > ValidationConstants.MaxScopeNameLength))
            errors.AddError("AllowedScopes",
                $"Scopes cannot exceed {ValidationConstants.MaxScopeNameLength} characters.");
        else
        {
            string? unknownScope = scopes.FirstOrDefault(scope => !validSystemScopes.Contains(scope));
            if (unknownScope is not null)
                errors.AddError("AllowedScopes",
                    $"Scope '{unknownScope}' is not a valid API scope or identity resource.");
        }

        if (errors.HasErrors)
            return await DenyClientCreationValidationFailureAsync(clientId, clientName, errors, cancellationToken);

        bool clientIdIsInUse = await _configurationDbContext.Clients
            .AsNoTracking()
            .AnyAsync(c => c.ClientId == clientId, cancellationToken);

        if (clientIdIsInUse)
            return await DenyClientIdInUseAsync(clientId!, clientName!, cancellationToken);

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
            AllowedGrantTypes = [],
            RedirectUris = [],
            PostLogoutRedirectUris = [],
            AllowedCorsOrigins = [],
            AllowedScopes = [],
            ClientSecrets = [],
            Properties = []
        };

        if (!string.IsNullOrWhiteSpace(input.SelectedPreset))
            client.Properties.Add(new ClientProperty
            {
                Key = PresetPropertyKey,
                Value = input.SelectedPreset
            });

        if (grantTypes.Count == 0)
            grantTypes = input.SelectedPreset == "m2m"
                ? ["client_credentials"]
                : ["authorization_code"];

        foreach (string grantType in grantTypes)
            client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = grantType });

        foreach (string uri in redirectUris) client.RedirectUris.Add(new ClientRedirectUri { RedirectUri = uri });

        foreach (string uri in postLogoutUris)
            client.PostLogoutRedirectUris.Add(new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = uri });

        foreach (string origin in corsOrigins) client.AllowedCorsOrigins.Add(new ClientCorsOrigin { Origin = origin });

        if (scopes.Count == 0 && input.SelectedPreset != "m2m")
        {
            if (!validSystemScopes.Contains("openid"))
                return await DenyMissingOpenIdAsync(clientId!, clientName!, cancellationToken);

            scopes.Add("openid");
            if (validSystemScopes.Contains("profile")) scopes.Add("profile");
        }

        foreach (string scope in scopes) client.AllowedScopes.Add(new ClientScope { Scope = scope });

        string? plaintextSecret = null;
        if (input.RequireClientSecret)
        {
            plaintextSecret = CryptoRandom.CreateUniqueId();
            client.ClientSecrets.Add(new ClientSecret
            {
                Description = "Initial client secret",
                Value = plaintextSecret.Sha256(),
                Type = SecretType.SharedSecret.ToSecretTypeValue(),
                Created = DateTime.UtcNow
            });
        }

        Duende.IdentityServer.Models.Client clientModel = client.ToModel();
        var validationContext = new ClientConfigurationValidationContext(clientModel);
        await _clientConfigurationValidator.ValidateAsync(validationContext, cancellationToken);

        if (!validationContext.IsValid)
            return await DenyInvalidClientConfigurationAsync(clientId!, clientName!, validationContext.ErrorMessage, cancellationToken);

        try
        {
            _configurationDbContext.Clients.Add(client);
            await _configurationDbContext.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.Client, AuditAction.Create, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                clientId, clientName,
                Details: $"Created client '{clientName}'"), cancellationToken);

            return ClientCreateResult.Succeeded(clientId!, plaintextSecret);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await AuditDeniedAsync(AuditReasonCode.NameCollision, clientId!, clientName!,
                $"A client with ID '{clientId}' already exists.", cancellationToken);
            return ClientCreateResult.Failed("ClientId", $"A client with ID '{clientId}' already exists.",
                AdminMutationStatus.Conflict);
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(clientId!, clientName!, ex, cancellationToken);
            throw;
        }
    }

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
            targetId, targetName,
            Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
    }

    private Task AuditDeniedAsync(AuditReasonCode reasonCode, string targetId, string targetName, string details,
        CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.Client, AuditAction.Create, AuditOutcome.Denied, reasonCode,
            targetId, targetName, Details: details), cancellationToken);

    private async Task<ClientCreateResult> DenySourceClientNotFoundAsync(
        string sourceClientId, string? clientId, string? clientName, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditReasonCode.NotFound, clientId ?? string.Empty, clientName ?? string.Empty,
            $"Source client '{sourceClientId}' was not found.", cancellationToken);
        return ClientCreateResult.Failed("Source client not found.");
    }

    private async Task<ClientCreateResult> DenyClientIdInUseAsync(string clientId, string clientName, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditReasonCode.NameCollision, clientId, clientName,
            $"A client with ID '{clientId}' already exists.", cancellationToken);
        return ClientCreateResult.Failed("ClientId", $"A client with ID '{clientId}' already exists.",
            AdminMutationStatus.Conflict);
    }

    private async Task<ClientCreateResult> DenyInvalidClientConfigurationAsync(
        string clientId, string clientName, string? errorMessage, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditReasonCode.ValidationFailed, clientId, clientName,
            errorMessage ?? "Invalid client configuration.", cancellationToken);
        return ClientCreateResult.Failed(errorMessage ?? "Invalid client configuration.");
    }

    private async Task<ClientCreateResult> DenyClientCreationValidationFailureAsync(
        string? clientId, string? clientName, ValidationErrorDictionary errors, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditReasonCode.ValidationFailed, clientId ?? string.Empty, clientName ?? string.Empty,
            "Client creation validation failed.", cancellationToken);
        return ClientCreateResult.Failed(errors);
    }

    private async Task<ClientCreateResult> DenyMissingOpenIdAsync(string clientId, string clientName, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(AuditReasonCode.ValidationFailed, clientId, clientName,
            "Client creation requires the 'openid' identity resource.", cancellationToken);
        return ClientCreateResult.Failed(
            "AllowedScopes",
            "The required 'openid' identity resource is not configured.");
    }
}