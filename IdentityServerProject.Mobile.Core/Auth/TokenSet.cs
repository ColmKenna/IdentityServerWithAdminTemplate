using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdentityServerProject.Mobile.Core.Auth;

public sealed record UserProfile(string Subject, string Name, string? Email, IReadOnlyList<string> Roles);

/// <summary>
///     What the device keeps after sign-in. The profile is stored with the tokens so the app can show who is
///     signed in, and let them work, with no network at all.
/// </summary>
public sealed record TokenSet(
    string AccessToken,
    string? RefreshToken,
    string? IdentityToken,
    DateTimeOffset AccessTokenExpiresAt,
    UserProfile User);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(TokenSet))]
internal sealed partial class TokenJsonContext : JsonSerializerContext;

/// <summary>
///     Platform seam over the OS credential store (Android Keystore, iOS Keychain). Implementations
///     must never throw on a missing or unreadable entry: they return null, so a corrupted store means
///     "signed out" rather than a crash at launch.
/// </summary>
public interface ISecureTokenStore
{
    Task<TokenSet?> LoadAsync();
    Task SaveAsync(TokenSet tokens);
    Task ClearAsync();
}
