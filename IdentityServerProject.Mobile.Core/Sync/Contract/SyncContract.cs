using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdentityServerProject.Mobile.Core.Sync.Contract;

// The wire contract between the app and a sync API. The template does not ship a server for it
// (see docs/mobile/README.md); these types are the specification one should be built against.
//
//   POST {ApiBaseAddress}/api/sync/push   PushRequest  -> PushResponse
//   GET  {ApiBaseAddress}/api/sync/pull?cursor={cursor}&limit={limit}   -> PullResponse
//
// Both require a bearer access token carrying the "api" scope. Records are identified by the
// client-generated Id, so a push that is retried after a lost response must be idempotent.

public enum SyncEntityKind
{
    Note
}

/// <summary>One record travelling in either direction.</summary>
/// <param name="Version">
///     Opaque server-assigned version. On push it is the version the device last saw (null for a record
///     the server has never seen); the server must answer <see cref="PushOutcome.Conflict" /> if it no
///     longer matches. On pull it is the record's current version.
/// </param>
/// <param name="Data">The record's fields, for example <see cref="NoteData" />; null for a delete.</param>
public sealed record SyncRecord(
    SyncEntityKind Kind,
    Guid Id,
    bool Deleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Version = null,
    JsonElement? Data = null);

public sealed record PushRequest(IReadOnlyList<SyncRecord> Changes);

public enum PushOutcome
{
    /// <summary>Stored. <see cref="PushResult.Version" /> is the record's new version.</summary>
    Accepted,

    /// <summary>The server holds a newer version. <see cref="PushResult.Current" /> carries it and replaces the device's copy.</summary>
    Conflict,

    /// <summary>The server refused the change (for example it failed validation). <see cref="PushResult.Error" /> says why; the device will not resend it until it is edited.</summary>
    Rejected
}

public sealed record PushResult(
    SyncEntityKind Kind,
    Guid Id,
    PushOutcome Outcome,
    string? Version = null,
    SyncRecord? Current = null,
    string? Error = null);

public sealed record PushResponse(IReadOnlyList<PushResult> Results);

/// <param name="NextCursor">Opaque position to resume from; send it back as <c>cursor</c>. Null when there is nothing to resume from yet.</param>
/// <param name="HasMore">True when the server has more changes than fit in this page.</param>
public sealed record PullResponse(IReadOnlyList<SyncRecord> Changes, string? NextCursor = null, bool HasMore = false);

/// <remarks>
///     Optional members default to null: they are omitted from the JSON when null, and with
///     <see cref="JsonSourceGenerationOptionsAttribute.RespectRequiredConstructorParameters" /> a parameter
///     without a default would make every such record unreadable.
/// </remarks>
public sealed record NoteData(
    string Title,
    string? Body = null);

/// <summary>
///     Source-generated, so serialisation does not depend on reflection that the iOS linker and
///     ahead-of-time compiler are allowed to remove. Strict about missing required members: a record
///     without a title must fail to read (and be skipped) rather than arrive as a null that later
///     breaks the whole batch at the database.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(SyncRecord))]
[JsonSerializable(typeof(PushRequest))]
[JsonSerializable(typeof(PushResponse))]
[JsonSerializable(typeof(PullResponse))]
[JsonSerializable(typeof(NoteData))]
public sealed partial class SyncJsonContext : JsonSerializerContext;
