using Microsoft.EntityFrameworkCore;
using IdentityServerProject.Mobile.Core.Domain;

namespace IdentityServerProject.Mobile.Core.Data;

/// <summary>
///     Owns the on-device SQLite file: applies migrations before first use, hands out short-lived
///     contexts, and wipes the data on sign-out. Contexts are never kept: in a Blazor Hybrid app the
///     dependency-injection scope lives as long as the app, so a scoped DbContext would never refresh.
/// </summary>
public sealed class LocalDatabase(IDbContextFactory<MobileDbContext> factory)
{
    public const string OwnerSubjectKey = "owner.subject";
    public const string PullCursorKey = "pull.cursor";
    public const string LastSyncedAtKey = "sync.lastSyncedAtTicks";

    private readonly SemaphoreSlim migrationGate = new(1, 1);
    private bool migrated;

    public async Task<MobileDbContext> OpenAsync(CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken);
        return await factory.CreateDbContextAsync(cancellationToken);
    }

    private async Task EnsureMigratedAsync(CancellationToken cancellationToken)
    {
        if (migrated) return;

        await migrationGate.WaitAsync(cancellationToken);
        try
        {
            if (migrated) return;

            // App updates ship new migrations, which run here on the user's existing data.
            await using MobileDbContext context = await factory.CreateDbContextAsync(cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);
            migrated = true;
        }
        finally
        {
            migrationGate.Release();
        }
    }

    public async Task<string?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        await using MobileDbContext context = await OpenAsync(cancellationToken);
        return await context.SyncMetadata.Where(m => m.Key == key).Select(m => m.Value).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SetMetadataAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        await using MobileDbContext context = await OpenAsync(cancellationToken);
        UpsertMetadata(context, key, value);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Stages the change on <paramref name="context" /> so it commits together with whatever else the caller saves.</summary>
    internal static void UpsertMetadata(MobileDbContext context, string key, string? value)
    {
        SyncMetadata? row = context.SyncMetadata.Find(key);
        if (row is null) context.SyncMetadata.Add(new SyncMetadata { Key = key, Value = value });
        else row.Value = value;
    }

    /// <summary>Counts what has not reached the server yet, and what the server has refused.</summary>
    public async Task<(int Pending, int Rejected)> CountUnsyncedAsync(CancellationToken cancellationToken = default)
    {
        await using MobileDbContext context = await OpenAsync(cancellationToken);

        // One entry per synced entity.
        (int Pending, int Rejected)[] counts =
        [
            await CountAsync(context.Notes, cancellationToken)
        ];

        return (counts.Sum(c => c.Pending), counts.Sum(c => c.Rejected));
    }

    private static async Task<(int Pending, int Rejected)> CountAsync<T>(DbSet<T> set, CancellationToken cancellationToken)
        where T : SyncEntity
    {
        // Tombstones count: a pending delete is still something the server has not been told.
        IQueryable<T> unsynced = set.IgnoreQueryFilters().Where(e => e.SyncStatus == SyncStatus.Pending);
        int rejected = await unsynced.CountAsync(e => e.SyncError != null, cancellationToken);
        int pending = await unsynced.CountAsync(e => e.SyncError == null, cancellationToken);
        return (pending, rejected);
    }

    /// <summary>Removes every record and all sync bookkeeping. The schema stays.</summary>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using MobileDbContext context = await OpenAsync(cancellationToken);
        await context.Notes.IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
        await context.SyncMetadata.ExecuteDeleteAsync(cancellationToken);
    }
}
