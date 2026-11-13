using System.Net.Http.Headers;
using IdentityServerProject.Mobile.Core.Sync;

namespace IdentityServerProject.Mobile.Core.Auth;

/// <summary>Attaches the current access token to outgoing API calls, refreshing it first if it is about to expire.</summary>
internal sealed class AccessTokenHandler(ISessionService session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? token = await session.GetAccessTokenAsync(cancellationToken: cancellationToken);

        if (token is null)
            throw session.State == SessionState.ReauthRequired
                ? new SyncAuthenticationException("The session needs to sign in again.")
                : new HttpRequestException("No access token is available right now.");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
