using System.Text.Json.Serialization;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Clients;

/// <summary>
///     Strongly typed value object representing a token lifetime in seconds, with unit conversions and boundary
///     validation.
/// </summary>
public readonly record struct TokenLifetime : IComparable<TokenLifetime>
{
    [JsonConstructor]
    public TokenLifetime(int seconds)
    {
        Seconds = seconds;
    }

    [JsonInclude] public int Seconds { get; }

    [JsonIgnore] public TimeSpan TotalTimeSpan => TimeSpan.FromSeconds(Seconds);

    public static TokenLifetime Zero => new(0);

    [JsonIgnore]
    public bool IsValidAccessToken =>
        Seconds >= ValidationConstants.MinAccessTokenLifetime &&
        Seconds <= ValidationConstants.MaxAccessTokenLifetime;

    [JsonIgnore]
    public bool IsValidIdentityToken =>
        Seconds >= ValidationConstants.MinIdentityTokenLifetime &&
        Seconds <= ValidationConstants.MaxIdentityTokenLifetime;

    [JsonIgnore]
    public bool IsValidRefreshToken =>
        Seconds >= ValidationConstants.MinRefreshTokenLifetime &&
        Seconds <= ValidationConstants.MaxAbsoluteRefreshTokenLifetime;

    [JsonIgnore]
    public bool IsValidSlidingRefreshToken =>
        Seconds >= ValidationConstants.MinRefreshTokenLifetime &&
        Seconds <= ValidationConstants.MaxSlidingRefreshTokenLifetime;

    public int CompareTo(TokenLifetime other) => Seconds.CompareTo(other.Seconds);

    public static TokenLifetime FromSeconds(int seconds) => new(seconds);

    public static TokenLifetime FromMinutes(int minutes) => new(minutes * 60);

    public static TokenLifetime FromHours(int hours) => new(hours * 3600);

    public static TokenLifetime FromDays(int days) => new(days * 86400);

    public static TokenLifetime FromTimeSpan(TimeSpan timeSpan) => new((int)timeSpan.TotalSeconds);

    public static implicit operator int(TokenLifetime lifetime) => lifetime.Seconds;

    public static explicit operator TokenLifetime(int seconds) => new(seconds);

    public static implicit operator TimeSpan(TokenLifetime lifetime) => lifetime.TotalTimeSpan;

    public static explicit operator TokenLifetime(TimeSpan timeSpan) => FromTimeSpan(timeSpan);

    public static bool operator <(TokenLifetime left, TokenLifetime right) => left.Seconds < right.Seconds;

    public static bool operator <=(TokenLifetime left, TokenLifetime right) => left.Seconds <= right.Seconds;

    public static bool operator >(TokenLifetime left, TokenLifetime right) => left.Seconds > right.Seconds;

    public static bool operator >=(TokenLifetime left, TokenLifetime right) => left.Seconds >= right.Seconds;

    public override string ToString() => $"{Seconds}s";
}