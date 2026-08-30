using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Clients;

public class ClientDetailsModel
{
    public required ClientId ClientId { get; set; }
    public required string ClientName { get; set; }
    public string? Description { get; set; }
    public required string ClientType { get; set; }
    public bool Enabled { get; set; }
    public bool RequirePkce { get; set; }
    public bool RequireClientSecret { get; set; }
    public bool RequireConsent { get; set; }
    public bool AllowOfflineAccess { get; set; }
    public TokenLifetime AccessTokenLifetime { get; set; }
    public required string AllowedGrantTypes { get; set; }
    public int RedirectUrisCount { get; set; }
    public int CorsOriginsCount { get; set; }
    public int SecretsCount { get; set; }
    public int AllowedScopesCount { get; set; }
    public List<string> AllowedScopes { get; set; } = new();
    public bool CanDelete { get; set; }
    public string? DeleteBlockReason { get; set; }
}

public class ClientDeleteResult
{
    public bool Success => Status == AdminMutationStatus.Succeeded;
    public AdminMutationStatus Status { get; set; }
    public string? ReasonCode { get; set; }
    public string? ErrorMessage { get; set; }

    public static ClientDeleteResult Succeeded() => new()
    {
        Status = AdminMutationStatus.Succeeded,
        ReasonCode = AuditReasonCode.Succeeded
    };

    public static ClientDeleteResult Failed(
        string errorMessage,
        AuditReasonCode? reasonCode = null,
        AdminMutationStatus status = AdminMutationStatus.Denied) =>
        new()
        {
            Status = status,
            ReasonCode = reasonCode ?? AuditReasonCode.ValidationFailed,
            ErrorMessage = errorMessage
        };
}

public class ClientBasicsModel
{
    public required ClientId ClientId { get; set; }
    public required string ClientName { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public string? ClientUri { get; set; }
    public string? LogoUri { get; set; }
}

public class ClientBasicsInputModel
{
    public required string ClientName { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public string? ClientUri { get; set; }
    public string? LogoUri { get; set; }
}

public sealed record ClientBasicsAuditValue(
    string ClientName,
    string? Description,
    bool Enabled,
    string? ClientUri,
    string? LogoUri) : IAuditValue;

public class ClientAuthenticationModel
{
    public required ClientId ClientId { get; set; }
    public required string ClientName { get; set; }
    public bool RequirePkce { get; set; }
    public bool RequireClientSecret { get; set; }
    public List<string> GrantTypes { get; set; } = new();
    public ClientEndpoints Endpoints { get; set; } = new();

    public List<string> RedirectUris
    {
        get => Endpoints.RedirectUris;
        set => Endpoints = Endpoints with { RedirectUris = value };
    }

    public List<string> PostLogoutRedirectUris
    {
        get => Endpoints.PostLogoutRedirectUris;
        set => Endpoints = Endpoints with { PostLogoutRedirectUris = value };
    }

    public List<string> AllowedCorsOrigins
    {
        get => Endpoints.AllowedCorsOrigins;
        set => Endpoints = Endpoints with { AllowedCorsOrigins = value };
    }

    public string? FrontChannelLogoutUri
    {
        get => Endpoints.FrontChannelLogoutUri;
        set => Endpoints = Endpoints with { FrontChannelLogoutUri = value };
    }

    public bool FrontChannelLogoutSessionRequired
    {
        get => Endpoints.FrontChannelLogoutSessionRequired;
        set => Endpoints = Endpoints with { FrontChannelLogoutSessionRequired = value };
    }

    public string? BackChannelLogoutUri
    {
        get => Endpoints.BackChannelLogoutUri;
        set => Endpoints = Endpoints with { BackChannelLogoutUri = value };
    }

    public bool BackChannelLogoutSessionRequired
    {
        get => Endpoints.BackChannelLogoutSessionRequired;
        set => Endpoints = Endpoints with { BackChannelLogoutSessionRequired = value };
    }

    public bool HasDrifted { get; set; }
    public string? DriftDetails { get; set; }
}

public class ClientAuthenticationInputModel
{
    public bool RequirePkce { get; set; }
    public bool RequireClientSecret { get; set; }
    public List<string> GrantTypes { get; set; } = new();
    public ClientEndpoints Endpoints { get; set; } = new();

    public List<string> RedirectUris
    {
        get => Endpoints.RedirectUris;
        set => Endpoints = Endpoints with { RedirectUris = value };
    }

    public List<string> PostLogoutRedirectUris
    {
        get => Endpoints.PostLogoutRedirectUris;
        set => Endpoints = Endpoints with { PostLogoutRedirectUris = value };
    }

    public List<string> CorsOrigins
    {
        get => Endpoints.AllowedCorsOrigins;
        set => Endpoints = Endpoints with { AllowedCorsOrigins = value };
    }

    public string? FrontChannelLogoutUri
    {
        get => Endpoints.FrontChannelLogoutUri;
        set => Endpoints = Endpoints with { FrontChannelLogoutUri = value };
    }

    public bool FrontChannelLogoutSessionRequired
    {
        get => Endpoints.FrontChannelLogoutSessionRequired;
        set => Endpoints = Endpoints with { FrontChannelLogoutSessionRequired = value };
    }

    public string? BackChannelLogoutUri
    {
        get => Endpoints.BackChannelLogoutUri;
        set => Endpoints = Endpoints with { BackChannelLogoutUri = value };
    }

    public bool BackChannelLogoutSessionRequired
    {
        get => Endpoints.BackChannelLogoutSessionRequired;
        set => Endpoints = Endpoints with { BackChannelLogoutSessionRequired = value };
    }
}

public sealed record ClientAuthenticationAuditValue(
    bool RequirePkce,
    bool RequireClientSecret,
    IReadOnlyList<string> GrantTypes,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> CorsOrigins,
    string? FrontChannelLogoutUri,
    bool FrontChannelLogoutSessionRequired,
    string? BackChannelLogoutUri,
    bool BackChannelLogoutSessionRequired) : IAuditValue;

public class ClientPermissionsModel
{
    public required ClientId ClientId { get; set; }
    public required string ClientName { get; set; }
    public bool IsInteractive { get; set; }
    public List<string> AllowedScopes { get; set; } = new();
    public List<string> AvailableIdentityScopes { get; set; } = new();
    public List<string> AvailableApiScopes { get; set; } = new();
}

public class ClientSecretSummary
{
    public int Id { get; set; }
    public string? Description { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Expiration { get; set; }
}

public class ClientSecretsModel
{
    public required ClientId ClientId { get; set; }
    public required string ClientName { get; set; }
    public bool RequireClientSecret { get; set; }
    public List<ClientSecretSummary> Secrets { get; set; } = new();
}

public class ClientSecretGenerateResult
{
    public bool Success => Status == AdminMutationStatus.Succeeded;
    public AdminMutationStatus Status { get; set; }
    public IReadOnlyDictionary<string, string[]> Errors { get; set; } = ValidationErrorDictionary.Empty;
    public string? ErrorMessage { get; set; }
    public string? PlaintextSecret { get; set; }

    public static ClientSecretGenerateResult Succeeded(string plaintextSecret) =>
        new() { Status = AdminMutationStatus.Succeeded, PlaintextSecret = plaintextSecret };

    public static ClientSecretGenerateResult ValidationFailure(string field, string errorMessage) =>
        new()
        {
            Status = AdminMutationStatus.ValidationFailed,
            Errors = new ValidationErrorDictionary().AddError(field, errorMessage),
            ErrorMessage = errorMessage
        };

    public static ClientSecretGenerateResult Failed(
        string errorMessage,
        AdminMutationStatus status = AdminMutationStatus.NotFound) =>
        new()
        {
            Status = status,
            Errors = new ValidationErrorDictionary().AddError(string.Empty, errorMessage),
            ErrorMessage = errorMessage
        };
}

public class ClientSecretRevokeResult
{
    public bool Success => Status == AdminMutationStatus.Succeeded;
    public AdminMutationStatus Status { get; set; }
    public string? ReasonCode { get; set; }
    public string? ErrorMessage { get; set; }

    public static ClientSecretRevokeResult Succeeded() => new()
    {
        Status = AdminMutationStatus.Succeeded,
        ReasonCode = AuditReasonCode.Succeeded
    };

    public static ClientSecretRevokeResult Failed(
        string errorMessage,
        AuditReasonCode? reasonCode = null,
        AdminMutationStatus status = AdminMutationStatus.Denied) =>
        new()
        {
            Status = status,
            ReasonCode = reasonCode ?? AuditReasonCode.ValidationFailed,
            ErrorMessage = errorMessage
        };
}

public class ClientTokenSettingsModel
{
    public required ClientId ClientId { get; set; }
    public required string ClientName { get; set; }
    public TokenLifetime AccessTokenLifetime { get; set; }
    public TokenLifetime IdentityTokenLifetime { get; set; }
    public bool RequireConsent { get; set; }
    public bool AllowOfflineAccess { get; set; }
    public RefreshTokenSettings RefreshToken { get; set; } = new();
}

public class ClientTokenSettingsInputModel
{
    public TokenLifetime AccessTokenLifetime { get; set; }
    public TokenLifetime IdentityTokenLifetime { get; set; }
    public bool RequireConsent { get; set; }
    public bool AllowOfflineAccess { get; set; }
    public RefreshTokenSettings RefreshToken { get; set; } = new();
}

public sealed record ClientTokenSettingsAuditValue(
    TokenLifetime AccessTokenLifetime,
    TokenLifetime IdentityTokenLifetime,
    bool RequireConsent,
    bool AllowOfflineAccess,
    RefreshTokenSettings RefreshToken) : IAuditValue;