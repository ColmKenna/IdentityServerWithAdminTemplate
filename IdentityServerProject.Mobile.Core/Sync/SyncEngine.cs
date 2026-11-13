using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using IdentityServerProject.Mobile.Core.Data;
using IdentityServerProject.Mobile.Core.Domain;
using IdentityServerProject.Mobile.Core.Sync.Contract;

namespace IdentityServerProject.Mobile.Core.Sync;

/// <param name="Accepted">Local changes the server stored.</param>
/// <param name="Conflicts">Local changes replaced by a newer server copy.</param>
/// <param name="Rejected">Local changes the server refused; they stay on the device flagged with the reason.</param>
/// <param name="Pulled">Server changes applied to the device.</param>
public sealed record SyncRunResult(int Accepted, int Conflicts, int Rejected, int Pulled);

/// <summary>
///     One push-then-pull exchange with the server. Policy, stated once here because it decides what a
///     user can lose: the server is the authority. A push the server reports as a conflict is replaced by
///     the server's copy, and a pull never overwrites a record that still has unpushed local edits (the
///     push that follows decides it). Edits made while a request is in flight are never marked synced by
///     that request.
/// </summary>
public sealed class SyncEngine(LocalDatabase database, ISyncApi api, LocalDataChanges changes, ILogger<SyncEngine> logger)
{
    public const int PushBatchSize = 100;
    public const int PullPageSize = 200;
    private const int MaxPushBatches = 50;
    private const int MaxPullPages = 100;

    public async Task<SyncRunResult> RunAsync(CancellationToken cancellationToken)
    {
        (int accepted, int conflicts, int rejected) = await PushAsync(cancellationToken);
        int pulled = await PullAsync(cancellationToken);

        if (accepted + conflicts + rejected + pulled > 0) changes.Raise(ChangeOrigin.Sync);
        return new SyncRunResult(accepted, conflicts, rejected, pulled);
    }

    private async Task<(int Accepted, int Conflicts, int Rejected)> PushAsync(CancellationToken cancellationToken)
    {
        int accepted = 0, conflicts = 0, rejected = 0;

        for (int batch = 0; batch < MaxPushBatches; batch++)
        {
            // Snapshot first, then talk to the server with no context open: the user keeps editing
            // while the request is in flight, and the result must be applied to what is stored by then.
            List<SyncRecord> records = await SnapshotPendingAsync(cancellationToken);
            if (records.Count == 0) break;

            PushResponse response = await api.PushAsync(new PushRequest(records), cancellationToken);
            Dictionary<(SyncEntityKind, Guid), DateTimeOffset> sent = records.ToDictionary(r => (r.Kind, r.Id), r => r.UpdatedAt);

            await using MobileDbContext context = await database.OpenAsync(cancellationToken);
            int progress = 0;

            foreach (PushResult result in response.Results)
            {
                if (!sent.TryGetValue((result.Kind, result.Id), out DateTimeOffset pushedUpdatedAt)) continue;

                SyncEntity? local = await FindAsync(context, result.Kind, result.Id, cancellationToken);
                switch (result.Outcome)
                {
                    case PushOutcome.Accepted:
                        if (local is not null) AcknowledgePush(context, local, result.Version, pushedUpdatedAt);
                        accepted++;
                        progress++;
                        break;

                    case PushOutcome.Conflict when result.Current is not null:
                        if (local is not null && local.UpdatedAt != pushedUpdatedAt)
                        {
                            // Edited again after this push was built: keep that newer edit, but rebase it on
                            // the server's version so the next push is accepted instead of conflicting again.
                            local.ServerVersion = result.Current.Version;
                        }
                        else
                        {
                            await ApplyRemoteAsync(context, result.Current, overwritePending: true, cancellationToken);
                            conflicts++;
                        }

                        progress++;
                        break;

                    default:
                        if (local is not null) local.SyncError = result.Error ?? "The server did not accept this change.";
                        rejected++;
                        progress++;
                        break;
                }
            }

            await context.SaveChangesAsync(cancellationToken);

            // A server that answers without acknowledging anything would otherwise be asked for the
            // same batch fifty times in a row.
            if (progress == 0) break;
        }

        return (accepted, conflicts, rejected);
    }

    private static void AcknowledgePush(MobileDbContext context, SyncEntity local, string? version, DateTimeOffset pushedUpdatedAt)
    {
        local.ServerVersion = version;
        if (local.UpdatedAt != pushedUpdatedAt) return; // edited since: still pending, now based on the new version

        if (local.IsDeleted) context.Remove(local);
        else
        {
            local.SyncStatus = SyncStatus.Synced;
            local.SyncError = null;
        }
    }

    private async Task<List<SyncRecord>> SnapshotPendingAsync(CancellationToken cancellationToken)
    {
        await using MobileDbContext context = await database.OpenAsync(cancellationToken);
        List<SyncRecord> batch = [];

        // One call per synced entity, parents before children, so the server sees a record before
        // the records that refer to it.
        await AddPendingAsync(context.Notes);

        return batch;

        async Task AddPendingAsync<T>(DbSet<T> set) where T : SyncEntity
        {
            if (batch.Count >= PushBatchSize) return;

            // Records the server refused are left out until the user edits them again.
            List<T> pending = await set.IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.SyncStatus == SyncStatus.Pending && e.SyncError == null)
                .OrderBy(e => e.UpdatedAt)
                .Take(PushBatchSize - batch.Count)
                .ToListAsync(cancellationToken);
            batch.AddRange(pending.Select(SyncRecordMapper.ToRecord));
        }
    }

    private async Task<int> PullAsync(CancellationToken cancellationToken)
    {
        string? cursor = await database.GetMetadataAsync(LocalDatabase.PullCursorKey, cancellationToken);
        int applied = 0;

        for (int page = 0; page < MaxPullPages; page++)
        {
            PullResponse response = await api.PullAsync(cursor, PullPageSize, cancellationToken);

            await using MobileDbContext context = await database.OpenAsync(cancellationToken);

            // The same record can appear more than once in a page; only its last state matters, and
            // applying them one by one would try to insert it twice.
            foreach (SyncRecord record in response.Changes.GroupBy(r => (r.Kind, r.Id)).Select(g => g.Last()))
            {
                try
                {
                    if (await ApplyRemoteAsync(context, record, overwritePending: false, cancellationToken)) applied++;
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                {
                    // One unreadable record must not stop the cursor advancing past it forever.
                    logger.LogWarning(ex, "Skipped {Kind} {Id}: its data could not be read.", record.Kind, record.Id);
                }
            }

            cursor = response.NextCursor ?? cursor;

            // Same transaction as the records, so the cursor can never run ahead of what was stored.
            LocalDatabase.UpsertMetadata(context, LocalDatabase.PullCursorKey, cursor);
            await context.SaveChangesAsync(cancellationToken);

            if (!response.HasMore) break;
        }

        return applied;
    }

    /// <returns>True if the device's copy changed.</returns>
    private static async Task<bool> ApplyRemoteAsync(
        MobileDbContext context, SyncRecord record, bool overwritePending, CancellationToken cancellationToken)
    {
        SyncEntity? local = await FindAsync(context, record.Kind, record.Id, cancellationToken);

        if (local is { SyncStatus: SyncStatus.Pending } && !overwritePending) return false;

        if (record.Deleted)
        {
            if (local is null) return false;
            context.Remove(local);
            return true;
        }

        if (local is not null)
        {
            SyncRecordMapper.ApplyTo(record, local);
            return true;
        }

        // Filled in before it is added: if the record is unreadable the exception must leave nothing
        // half-built in the context for the next SaveChanges to store.
        SyncEntity created = Create(record.Kind);
        SyncRecordMapper.ApplyTo(record, created);
        context.Add(created);
        return true;
    }

    private static async Task<SyncEntity?> FindAsync(
        MobileDbContext context, SyncEntityKind kind, Guid id, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case SyncEntityKind.Note:
                return await context.Notes.IgnoreQueryFilters().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
            default:
                throw new InvalidOperationException($"Unknown record kind {kind}.");
        }
    }

    private static SyncEntity Create(SyncEntityKind kind) => kind switch
    {
        SyncEntityKind.Note => new Note(),
        _ => throw new InvalidOperationException($"Unknown record kind {kind}.")
    };
}
