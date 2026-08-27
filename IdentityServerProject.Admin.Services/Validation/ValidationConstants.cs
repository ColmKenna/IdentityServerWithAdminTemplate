namespace IdentityServerProject.Services.Validation;

public static class ValidationConstants
{
    public const int MaxNameLength = 200;
    public const int MaxDisplayNameLength = 200;
    public const int MaxDescriptionLength = 1000;
    public const int MaxSecretDescriptionLength = 1000;
    public const int MaxClientSecretDescriptionLength = 2000;
    public const int MaxSecretTypeLength = 250;
    public const int MaxScopeNameLength = 200;
    public const int MaxClaimTypeLength = 200;
    public const int MaxClaimValueLength = 2000;
    public const int MaxClientIdLength = 200;
    public const int MaxClientRedirectUriLength = 400;
    public const int MaxClientPostLogoutRedirectUriLength = 400;
    public const int MaxClientCorsOriginLength = 150;
    public const int MaxGrantTypeLength = 250;
    public const int MinAccessTokenLifetime = 60;
    public const int MaxAccessTokenLifetime = 86400;
    public const int MinIdentityTokenLifetime = 60;
    public const int MaxIdentityTokenLifetime = 3600;
    public const int MinRefreshTokenLifetime = 60;
    public const int MaxAbsoluteRefreshTokenLifetime = 31536000;
    public const int MaxSlidingRefreshTokenLifetime = 15552000;
    public const int MaxLogoutUriLength = 2000;
}