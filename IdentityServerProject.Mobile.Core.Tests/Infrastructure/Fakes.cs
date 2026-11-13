using IdentityServerProject.Mobile.Core.Auth;
using IdentityServerProject.Mobile.Core.Sync;
using IdentityServerProject.Mobile.Core.Sync.Contract;

namespace IdentityServerProject.Mobile.Core.Tests.Infrastructure;

public sealed class FakeNetworkStatus : INetworkStatus
{
    private bool online = true;

    public bool IsOnline
    {
        get => online;
        set
        {
            if (online == value) return;
            online = value;
            Changed?.Invoke(value);
        }
    }

    public event Action<bool>? Changed;
}

public sealed class FakeTokenStore : ISecureTokenStore
{
    public TokenSet? Stored { get; set; }
    public int SaveCount { get; private set; }

    public Task<TokenSet?> LoadAsync() => Task.FromResult(Stored);

    public Task SaveAsync(TokenSet tokens)
    {
        Stored = tokens;
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        Stored = null;
        return Task.CompletedTask;
    }
}

public sealed class FakeOidcGateway : IOidcGateway
{
    public Func<AuthResult> OnSignIn { get; set; } = () => AuthResult.Failed(AuthFailure.Other, "Sign-in is not configured for this test.");
    public Func<TokenSet, Task<AuthResult>> OnRefresh { get; set; } = _ => Task.FromResult(AuthResult.Failed(AuthFailure.Unreachable));
    public Func<TokenSet?, Task> OnSignOut { get; set; } = _ => Task.CompletedTask;

    private int signInCalls;
    private int refreshCalls;
    private int signOutCalls;

    public int SignInCalls => signInCalls;
    public int RefreshCalls => refreshCalls;
    public int SignOutCalls => signOutCalls;

    public Task<AuthResult> SignInAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref signInCalls);
        return Task.FromResult(OnSignIn());
    }

    public Task<AuthResult> RefreshAsync(TokenSet current, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref refreshCalls);
        return OnRefresh(current);
    }

    public Task SignOutAsync(TokenSet? tokens, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref signOutCalls);
        return OnSignOut(tokens);
    }
}

public sealed class FakeSyncApi : ISyncApi
{
    public List<PushRequest> Pushes { get; } = [];
    public List<(string? Cursor, int Limit)> Pulls { get; } = [];

    public Func<PushRequest, PushResponse> OnPush { get; set; } = request => new PushResponse([]);
    public Func<string?, int, PullResponse> OnPull { get; set; } = (_, _) => new PullResponse([], null, false);

    public Task<PushResponse> PushAsync(PushRequest request, CancellationToken cancellationToken)
    {
        Pushes.Add(request);
        return Task.FromResult(OnPush(request));
    }

    public Task<PullResponse> PullAsync(string? cursor, int limit, CancellationToken cancellationToken)
    {
        Pulls.Add((cursor, limit));
        return Task.FromResult(OnPull(cursor, limit));
    }
}
