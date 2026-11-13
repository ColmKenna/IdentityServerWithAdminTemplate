using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace IdentityServerProject.Mobile.Core.Auth;

/// <summary>The only thing the platform has to provide for token storage: get, set and remove one string.</summary>
public interface ISecureStringStorage
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    void Remove(string key);
}

/// <summary>
///     Stores the <see cref="TokenSet" /> as one JSON value in the platform's credential store.
///     Reading is forgiving on purpose: Android's Keystore can lose its key (a restored backup, a changed
///     lock screen) and the stored value then cannot be decrypted. That has to surface as "signed out",
///     not as an exception in the middle of app launch.
/// </summary>
public sealed class SecureStringTokenStore(ISecureStringStorage storage, ILogger<SecureStringTokenStore> logger) : ISecureTokenStore
{
    private const string Key = "mobile.tokens";

    public async Task<TokenSet?> LoadAsync()
    {
        try
        {
            string? json = await storage.GetAsync(Key);
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize(json, TokenJsonContext.Default.TokenSet);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The stored session could not be read; treating the device as signed out.");
            storage.Remove(Key);
            return null;
        }
    }

    public Task SaveAsync(TokenSet tokens) =>
        storage.SetAsync(Key, JsonSerializer.Serialize(tokens, TokenJsonContext.Default.TokenSet));

    public Task ClearAsync()
    {
        storage.Remove(Key);
        return Task.CompletedTask;
    }
}
