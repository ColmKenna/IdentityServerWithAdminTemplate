using System.Text.Json;
using IdentityServerProject.Mobile.Core.Domain;
using IdentityServerProject.Mobile.Core.Sync.Contract;

namespace IdentityServerProject.Mobile.Core.Sync;

/// <summary>Translates between stored entities and wire records. Pure, so it is exercised without a database.</summary>
internal static class SyncRecordMapper
{
    public static SyncRecord ToRecord(SyncEntity entity) => entity switch
    {
        Note n => Build(SyncEntityKind.Note, n, new NoteData(n.Title, n.Body), SyncJsonContext.Default.NoteData),
        _ => throw new NotSupportedException($"{entity.GetType().Name} cannot be synced.")
    };

    /// <summary>Copies the record's fields and bookkeeping onto <paramref name="entity" /> and marks it synced.</summary>
    public static void ApplyTo(SyncRecord record, SyncEntity entity)
    {
        if (record.Data is not { } data)
            throw new InvalidOperationException($"{record.Kind} {record.Id} carries no data and is not a delete.");

        switch (entity)
        {
            case Note n:
                NoteData nd = Read(data, SyncJsonContext.Default.NoteData);
                (n.Title, n.Body) = (nd.Title, nd.Body);
                break;
            default:
                throw new NotSupportedException($"{entity.GetType().Name} cannot be synced.");
        }

        entity.Id = record.Id;
        entity.CreatedAt = record.CreatedAt;
        entity.UpdatedAt = record.UpdatedAt;
        entity.ServerVersion = record.Version;
        entity.IsDeleted = false;
        entity.SyncStatus = SyncStatus.Synced;
        entity.SyncError = null;
    }

    private static SyncRecord Build<TData>(
        SyncEntityKind kind,
        SyncEntity entity,
        TData data,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TData> typeInfo) =>
        new(kind,
            entity.Id,
            entity.IsDeleted,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.ServerVersion,
            entity.IsDeleted ? null : JsonSerializer.SerializeToElement(data, typeInfo));

    private static TData Read<TData>(JsonElement element, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TData> typeInfo) =>
        element.Deserialize(typeInfo) ?? throw new InvalidOperationException("The record's data was empty.");
}
