using Duende.IdentityServer.Models;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Clients;

/// <summary>
/// Cohesive domain representation of refresh token behavior and lifetimes.
/// </summary>
public sealed record RefreshTokenSettings
{
    public TokenUsage Usage { get; init; } = TokenUsage.OneTimeOnly;

    public TokenExpiration Expiration { get; init; } = TokenExpiration.Absolute;

    public TokenLifetime AbsoluteLifetime { get; init; } = TokenLifetime.FromDays(30);

    public TokenLifetime SlidingLifetime { get; init; } = TokenLifetime.FromDays(15);

    public bool IsSlidingValid => SlidingLifetime <= AbsoluteLifetime;

    public bool IsAbsoluteLifetimeValid =>
        AbsoluteLifetime.Seconds >= ValidationConstants.MinRefreshTokenLifetime &&
        AbsoluteLifetime.Seconds <= ValidationConstants.MaxAbsoluteRefreshTokenLifetime;

    public bool IsSlidingLifetimeValid =>
        SlidingLifetime.Seconds >= ValidationConstants.MinRefreshTokenLifetime &&
        SlidingLifetime.Seconds <= ValidationConstants.MaxSlidingRefreshTokenLifetime;
}
