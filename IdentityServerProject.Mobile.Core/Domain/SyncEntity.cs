namespace IdentityServerProject.Mobile.Core.Domain;

public enum SyncStatus
{
    /// <summary>The server has seen this exact version of the record.</summary>
    Synced = 0,

    /// <summary>Created, edited or deleted on this device since the server last saw it.</summary>
    Pending = 1
}

/// <summary>
///     Base for everything the device stores and syncs. Ids are generated on the device so records can
///     be created offline. Deletes are tombstones (<see cref="IsDeleted" />) until the server has been
///     told, because a row that is simply gone cannot be reported as deleted later.
/// </summary>
public abstract class SyncEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public SyncStatus SyncStatus { get; set; }

    /// <summary>Opaque version the server returned last time; sent back as the base of the next edit.</summary>
    public string? ServerVersion { get; set; }

    /// <summary>Why the server refused the last push. Cleared by the next local edit.</summary>
    public string? SyncError { get; set; }

    public void MarkPending()
    {
        SyncStatus = SyncStatus.Pending;
        SyncError = null;
    }
}
