namespace IdentityServerProject.Mobile.Core.Auth;

public enum SessionState
{
    /// <summary>No account on this device.</summary>
    SignedOut,

    /// <summary>An account is on this device. Says nothing about connectivity: this is the state the app is in offline.</summary>
    SignedIn,

    /// <summary>
    ///     The identity server no longer accepts this device's refresh token. Local data stays usable;
    ///     only syncing is blocked until the user signs in again.
    /// </summary>
    ReauthRequired
}

public interface ISessionService
{
    SessionState State { get; }

    /// <summary>The account on this device, available offline. Null when <see cref="State" /> is <see cref="SessionState.SignedOut" />.</summary>
    UserProfile? User { get; }

    event Action? Changed;

    /// <summary>Loads the stored session. Local only, never touches the network. Safe to call repeatedly.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<AuthResult> SignInAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Removes the account and all local data from the device, then tells the server if it can.
    ///     Works offline; unsynced changes are lost, so callers confirm with the user first.
    /// </summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     A bearer token for the API, refreshing it if it is about to expire. Null when there is no usable
    ///     token right now (signed out, offline, or the refresh was refused) — callers treat that as "not now".
    /// </summary>
    Task<string?> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
}
