using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using IdentityServerProject.Mobile.Core.Domain;

namespace IdentityServerProject.Mobile.Core.Services;

/// <summary>
///     The bookkeeping every local write shares: assigning ids and timestamps, queueing the change
///     for sync, and turning a delete into either a tombstone or a plain removal.
/// </summary>
internal static class RecordStamps
{
    public static void ForCreate(SyncEntity entity, DateTimeOffset now)
    {
        entity.Id = Guid.CreateVersion7(now);
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        entity.IsDeleted = false;
        entity.ServerVersion = null;
        entity.MarkPending();
    }

    public static void ForEdit(SyncEntity entity, DateTimeOffset now)
    {
        entity.UpdatedAt = now;
        entity.MarkPending();
    }

    /// <summary>
    ///     A record the server has never acknowledged has nothing to be deleted from, so it is simply
    ///     removed. One the server knows about stays as a tombstone until a push reports the delete.
    ///     There are no foreign keys to cascade, so a service deleting a parent calls this for each child too.
    /// </summary>
    public static void Delete<T>(DbSet<T> set, T entity, DateTimeOffset now) where T : SyncEntity
    {
        if (entity.ServerVersion is null)
        {
            set.Remove(entity);
            return;
        }

        entity.IsDeleted = true;
        ForEdit(entity, now);
    }

    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public static void ValidateOrThrow(object entity) =>
        Validator.ValidateObject(entity, new ValidationContext(entity), validateAllProperties: true);
}

internal static class LikePattern
{
    public const string Escape = "\\";

    /// <summary>A LIKE pattern matching <paramref name="term" /> anywhere, with the user's % and _ taken literally.</summary>
    public static string Contains(string term) =>
        "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
