using IdentityServerProject.Mobile.Core.Sync;

namespace IdentityServerProject.Mobile.Services;

/// <summary>MAUI has no hosted services; this starts the sync coordinator once the app's services exist.</summary>
internal sealed class SyncStartup : IMauiInitializeService
{
    public void Initialize(IServiceProvider services) => services.GetRequiredService<SyncCoordinator>().Start();
}
