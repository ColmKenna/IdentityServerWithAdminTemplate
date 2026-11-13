using IdentityServerProject.Mobile.Core.Sync;

namespace IdentityServerProject.Mobile.Services;

internal sealed class MauiNetworkStatus : INetworkStatus
{
    public MauiNetworkStatus() => Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;

    // Constrained (captive portal) counts as offline: the server cannot be reached through it.
    public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public event Action<bool>? Changed;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) =>
        Changed?.Invoke(e.NetworkAccess == NetworkAccess.Internet);
}
