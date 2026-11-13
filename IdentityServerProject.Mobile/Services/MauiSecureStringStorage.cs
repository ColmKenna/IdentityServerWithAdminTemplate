using IdentityServerProject.Mobile.Core.Auth;

namespace IdentityServerProject.Mobile.Services;

/// <summary>
///     Android Keystore-backed encrypted preferences / iOS Keychain, via MAUI SecureStorage. This is the only
///     place the refresh token is ever written. Errors are left to <c>SecureStringTokenStore</c>, which turns an
///     unreadable entry into "signed out".
/// </summary>
internal sealed class MauiSecureStringStorage : ISecureStringStorage
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public void Remove(string key) => SecureStorage.Default.Remove(key);
}
