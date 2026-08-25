using System;
using System.Collections.Generic;
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
    public bool Success { get; set; }
    public AdminMutationStatus Status { get; set; }
    public string? ReasonCode { get; set; }
    public string? ErrorMessage { get; set; }

    public static ClientDeleteResult Succeeded() => new()
    {
        Success = true,
        Status = AdminMutationStatus.Succeeded,
        ReasonCode = AuditReasonCodes.Succeeded
    };

    public static ClientDeleteResult Failed(
        string errorMessage,
        string reasonCode = AuditReasonCodes.ValidationFailed,
        AdminMutationStatus status = AdminMutationStatus.Denied) =>
        new() { Success = false, Status = status, ReasonCode = reasonCode, ErrorMessage = errorMessage };
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
    string? LogoUri) : AuditLogs.IAuditValue;

public class ClientAuthenticationModel
{
    public required ClientId ClientId { get; set; }
    public required string ClientName { get; set; }
    public bool RequirePkce { get; set; }
    public bool RequireClientSecret { get; set; }
    public List<string> GrantTypes { get; set; } = new();
    public List<string> RedirectUris { get; set; } = new();
    public List<string> PostLogoutRedirectUris { get; set; } = new();
    public List<string> AllowedCorsOrigins { get; set; } = new();
    public string? FrontChannelLogoutUri { get; set; }
    public bool FrontChannelLogoutSessionRequired { get; set; }
    public string? BackChannelLogoutUri { get; set; }
    public bool BackChannelLogoutSessionRequired { get; set; }
    public bool HasDrifted { get; set; }
    public string? DriftDetails { get; set; }
}

public class ClientAuthenticationInputModel
{
    public bool RequirePkce { get; set; }
    public bool RequireClientSecret { get; set; }
    public List<string> GrantTypes { get; set; } = new();
    public List<string> RedirectUris { get; set; } = new();
    public List<string> PostLogoutRedirectUris { get; set; } = new();
    public List<string> CorsOrigins { get; set; } = new();
    public string? FrontChannelLogoutUri { get; set; }
    public bool FrontChannelLogoutSessionRequired { get; set; }
    public string? BackChannelLogoutUri { get; set; }
    public bool BackChannelLogoutSessionRequired { get; set; }
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
    bool BackChannelLogoutSessionRequired) : AuditLogs.IAuditValue;

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
    public bool Success { get; set; }
    public AdminMutationStatus Status { get; set; }
    public IReadOnlyDictionary<string, string[]> Errors { get; set; } = new Dictionary<string, string[]>();
    public string? ErrorMessage { get; set; }
    public string? PlaintextSecret { get; set; }

    public static ClientSecretGenerateResult Succeeded(string plaintextSecret) =>
        new() { Success = true, Status = AdminMutationStatus.Succeeded, PlaintextSecret = plaintextSecret };

    public static ClientSecretGenerateResult ValidationFailure(string field, string errorMessage) =>
        new()
        {
            Success = false,
            Status = AdminMutationStatus.ValidationFailed,
            Errors = new Dictionary<string, string[]> { [field] = new[] { errorMessage } },
            ErrorMessage = errorMessage
        };

    public static ClientSecretGenerateResult Failed(
        string errorMessage,
        AdminMutationStatus status = AdminMutationStatus.NotFound) =>
        new()
        {
            Success = false,
            Status = status,
            Errors = new Dictionary<string, string[]> { [string.Empty] = new[] { errorMessage } },
            ErrorMessage = errorMessage
        };
}

public class ClientSecretRevokeResult
{
    public bool Success { get; set; }
    public AdminMutationStatus Status { get; set; }
    public string? ReasonCode { get; set; }
    public string? ErrorMessage { get; set; }

    public static ClientSecretRevokeResult Succeeded() => new()
    {
        Success = true,
        Status = AdminMutationStatus.Succeeded,
        ReasonCode = AuditReasonCodes.Succeeded
    };

    public static ClientSecretRevokeResult Failed(
        string errorMessage,
        string reasonCode = AuditReasonCodes.ValidationFailed,
        AdminMutationStatus status = AdminMutationStatus.Denied) =>
        new() { Success = false, Status = status, ReasonCode = reasonCode, ErrorMessage = errorMessage };
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
    RefreshTokenSettings RefreshToken) : AuditLogs.IAuditValue;
