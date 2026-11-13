namespace IdentityServerProject.Mobile.Core.Sync;

/// <summary>
///     Whether the device has a connection. This is a hint, never a guarantee: a phone on captive-portal
///     Wi-Fi reports online and still cannot reach the server, so every network call is still prepared to fail.
/// </summary>
public interface INetworkStatus
{
    bool IsOnline { get; }

    /// <summary>Raised with the new state whenever connectivity changes. May fire on any thread.</summary>
    event Action<bool>? Changed;
}
