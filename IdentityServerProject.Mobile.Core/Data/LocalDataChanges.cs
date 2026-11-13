namespace IdentityServerProject.Mobile.Core.Data;

public enum ChangeOrigin
{
    /// <summary>The user created, edited or deleted something on this device.</summary>
    Local,

    /// <summary>A sync run applied server changes. Must not trigger another sync.</summary>
    Sync
}

/// <summary>
///     Tells screens the local data changed so they can reload. The origin matters: the sync
///     coordinator reacts to <see cref="ChangeOrigin.Local" /> edits only, otherwise applying a pull
///     would schedule another sync, whose pull would schedule another, indefinitely.
/// </summary>
public sealed class LocalDataChanges
{
    public event Action<ChangeOrigin>? Changed;

    public void Raise(ChangeOrigin origin) => Changed?.Invoke(origin);
}
