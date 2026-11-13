using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace IdentityServerProject.Mobile.Core.Auth;

/// <summary>
///     Presents the on-device session to Blazor's authorization system. The principal is built from the
///     stored profile, so a signed-in user is "authenticated" with no network. A session whose refresh
///     token was refused still counts: the user can keep working locally, and the sync banner tells them
///     to sign in again.
/// </summary>
public sealed class SessionAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    public const string AuthenticationType = "Mobile.Device";

    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly ISessionService session;

    public SessionAuthenticationStateProvider(ISessionService session)
    {
        this.session = session;
        session.Changed += OnSessionChanged;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // Reads the credential store, which is the slow part of launch. The router shows its
        // "Authorizing" placeholder until this completes.
        await session.InitializeAsync();
        return Build();
    }

    public void Dispose() => session.Changed -= OnSessionChanged;

    private void OnSessionChanged() => NotifyAuthenticationStateChanged(Task.FromResult(Build()));

    private AuthenticationState Build()
    {
        if (session.User is not { } user) return Anonymous;

        List<Claim> claims =
        [
            new("sub", user.Subject),
            new(ClaimTypes.NameIdentifier, user.Subject),
            new(ClaimTypes.Name, user.Name)
        ];
        if (user.Email is not null) claims.Add(new Claim(ClaimTypes.Email, user.Email));
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType)));
    }
}
