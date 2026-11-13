using Microsoft.Extensions.Logging;
using IdentityServerProject.Mobile.Core.Data;

namespace IdentityServerProject.Mobile.Core.Auth;

public sealed class SessionService(
    ISecureTokenStore store,
    IOidcGateway gateway,
    LocalDatabase database,
    TimeProvider time,
    ILogger<SessionService> logger) : ISessionService
{
    /// <summary>Refresh a little early so a token does not expire between being handed out and arriving.</summary>
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim initializeGate = new(1, 1);

    // One refresh at a time. The identity server rotates the refresh token on every use, so two
    // concurrent refreshes would spend the same token twice and the second would be refused.
    private readonly SemaphoreSlim refreshGate = new(1, 1);

    private TokenSet? tokens;
    private bool initialized;

    public SessionState State { get; private set; } = SessionState.SignedOut;

    public UserProfile? User => tokens?.User;

    public event Action? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized) return;

        await initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (initialized) return;

            tokens = await store.LoadAsync();
            State = tokens is null ? SessionState.SignedOut : SessionState.SignedIn;
            initialized = true;
        }
        finally
        {
            initializeGate.Release();
        }

        Changed?.Invoke();
    }

    public async Task<AuthResult> SignInAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        AuthResult result = await gateway.SignInAsync(cancellationToken);
        if (result.Tokens is not { } issued) return result;

        // Data belongs to whoever synced it. If a different account signs in on this device (the old
        // session lapsed and someone else used the browser), the previous account's records go.
        string? owner = await database.GetMetadataAsync(LocalDatabase.OwnerSubjectKey, cancellationToken);
        if (owner is not null && owner != issued.User.Subject)
        {
            logger.LogWarning("A different account signed in; clearing the previous account's local data.");
            await database.ClearAsync(cancellationToken);
        }

        await database.SetMetadataAsync(LocalDatabase.OwnerSubjectKey, issued.User.Subject, cancellationToken);
        await store.SaveAsync(issued);

        tokens = issued;
        State = SessionState.SignedIn;
        Changed?.Invoke();
        return result;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        TokenSet? previous = tokens;

        // Local first and unconditional: signing out must work with no network and no server.
        tokens = null;
        State = SessionState.SignedOut;
        await store.ClearAsync();
        await database.ClearAsync(cancellationToken);
        Changed?.Invoke();

        if (previous is null) return;

        try
        {
            await gateway.SignOutAsync(previous, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation(ex, "Server sign-out did not complete; the device is signed out regardless.");
        }
    }

    public async Task<string?> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        if (tokens is { } current && !forceRefresh && !IsExpiring(current)) return current.AccessToken;
        if (tokens is null) return null;

        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have refreshed while this one waited.
            if (tokens is not { } latest) return null;
            if (!forceRefresh && !IsExpiring(latest)) return latest.AccessToken;

            if (latest.RefreshToken is null)
            {
                MarkReauthRequired();
                return null;
            }

            AuthResult result = await gateway.RefreshAsync(latest, cancellationToken);

            // Signed out while the request was in flight: do not bring the session back.
            if (tokens is null) return null;

            if (result.Tokens is { } refreshed)
            {
                tokens = refreshed;
                await store.SaveAsync(refreshed);
                if (State != SessionState.SignedIn)
                {
                    State = SessionState.SignedIn;
                    Changed?.Invoke();
                }

                return refreshed.AccessToken;
            }

            // Only a definite "no" from the server ends the session's ability to sync. Not reaching
            // it — which is the normal situation offline — leaves everything as it was.
            if (result.Failure == AuthFailure.Rejected) MarkReauthRequired();
            else logger.LogInformation("Token refresh failed ({Failure}); keeping the session.", result.Failure);

            return null;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private bool IsExpiring(TokenSet set) => set.AccessTokenExpiresAt - ExpirySkew <= time.GetUtcNow();

    private void MarkReauthRequired()
    {
        if (State == SessionState.ReauthRequired) return;

        State = SessionState.ReauthRequired;
        Changed?.Invoke();
    }
}
