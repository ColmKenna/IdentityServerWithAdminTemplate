using IdentityServerProject.Mobile.Core.Sync.Contract;

namespace IdentityServerProject.Mobile.Core.Sync;

public interface ISyncApi
{
    /// <exception cref="SyncAuthenticationException">The server rejected the access token.</exception>
    /// <exception cref="HttpRequestException">The server could not be reached or answered with an error.</exception>
    Task<PushResponse> PushAsync(PushRequest request, CancellationToken cancellationToken);

    /// <exception cref="SyncAuthenticationException">The server rejected the access token.</exception>
    /// <exception cref="HttpRequestException">The server could not be reached or answered with an error.</exception>
    Task<PullResponse> PullAsync(string? cursor, int limit, CancellationToken cancellationToken);
}

/// <summary>The sync API answered 401 or 403. The session may need refreshing or the user may need to sign in again.</summary>
public sealed class SyncAuthenticationException(string message) : Exception(message);
