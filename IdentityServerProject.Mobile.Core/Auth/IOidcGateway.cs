namespace IdentityServerProject.Mobile.Core.Auth;

public enum AuthFailure
{
    None,

    /// <summary>The user closed the browser before finishing.</summary>
    Cancelled,

    /// <summary>The identity server could not be reached. Says nothing about whether the credentials are good.</summary>
    Unreachable,

    /// <summary>The identity server answered and refused: the grant is revoked, expired or otherwise invalid.</summary>
    Rejected,

    Other
}

public sealed record AuthResult(TokenSet? Tokens, AuthFailure Failure, string? Message)
{
    public bool Succeeded => Tokens is not null;

    public static AuthResult Success(TokenSet tokens) => new(tokens, AuthFailure.None, null);

    public static AuthResult Failed(AuthFailure failure, string? message = null) => new(null, failure, message);
}

/// <summary>The protocol conversation with IdentityServer, behind an interface so session logic can be tested without a browser.</summary>
public interface IOidcGateway
{
    /// <summary>Runs authorization code + PKCE in the system browser.</summary>
    Task<AuthResult> SignInAsync(CancellationToken cancellationToken);

    /// <summary>Exchanges the refresh token for new tokens. Never returns a different user.</summary>
    Task<AuthResult> RefreshAsync(TokenSet current, CancellationToken cancellationToken);

    /// <summary>Best effort: revokes the refresh token and ends the identity server session. Failure is not an error.</summary>
    Task SignOutAsync(TokenSet? tokens, CancellationToken cancellationToken);
}
