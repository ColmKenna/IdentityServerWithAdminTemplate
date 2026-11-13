using System.Text.Json;
using IdentityServerProject.Mobile.Core.Data;
using IdentityServerProject.Mobile.Core.Domain;
using IdentityServerProject.Mobile.Core.Sync;
using IdentityServerProject.Mobile.Core.Sync.Contract;
using IdentityServerProject.Mobile.Core.Tests.Infrastructure;

namespace IdentityServerProject.Mobile.Core.Tests.Data;

public sealed class LocalDatabaseAndMapperTests : IAsyncLifetime
{
    private readonly ClientHarness app = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await app.DisposeAsync();

    [Fact]
    public async Task FirstUse_CreatesTheSchemaByApplyingMigrations()
    {
        await using MobileDbContext context = await app.Database.OpenAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task TheModel_HasNoChangesMissingFromTheMigrations()
    {
        await using MobileDbContext context = await app.Database.OpenAsync();

        // Fails when an entity or mapping was changed without running `dotnet ef migrations add`.
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Metadata_IsStoredAndOverwritten()
    {
        Assert.Null(await app.Database.GetMetadataAsync("k"));

        await app.Database.SetMetadataAsync("k", "one");
        await app.Database.SetMetadataAsync("k", "two");

        Assert.Equal("two", await app.Database.GetMetadataAsync("k"));
    }

    [Fact]
    public async Task CountUnsynced_SeparatesWaitingFromRefused_AndCountsPendingDeletes()
    {
        Note waiting = await app.AddNoteAsync("Waiting");
        Note refused = await app.AddNoteAsync("Refused");
        Note synced = await app.AddNoteAsync("Synced");
        Note deleted = await app.AddNoteAsync("Deleted");
        await app.MarkEverythingSyncedAsync();

        await using (MobileDbContext context = await app.Database.OpenAsync())
        {
            Note w = await context.Notes.SingleAsync(n => n.Id == waiting.Id);
            w.MarkPending();
            Note r = await context.Notes.SingleAsync(n => n.Id == refused.Id);
            r.MarkPending();
            r.SyncError = "No";
            await context.SaveChangesAsync();
        }

        await app.Notes.DeleteAsync(deleted.Id);

        (int pending, int rejected) = await app.Database.CountUnsyncedAsync();

        Assert.Equal(2, pending); // the edit and the delete
        Assert.Equal(1, rejected);
        Assert.Equal(SyncStatus.Synced, (await app.RawAsync<Note>(synced.Id))!.SyncStatus);
    }

    [Fact]
    public async Task Clear_RemovesEveryRecordTombstonesAndBookkeeping()
    {
        Note kept = await app.AddNoteAsync("Kept");
        Note deleted = await app.AddNoteAsync("Deleted");
        await app.MarkEverythingSyncedAsync();
        await app.Notes.DeleteAsync(deleted.Id); // leaves a tombstone
        await app.Database.SetMetadataAsync(LocalDatabase.PullCursorKey, "cursor");

        await app.Database.ClearAsync();

        await using MobileDbContext context = await app.Database.OpenAsync();
        Assert.Empty(await context.Notes.IgnoreQueryFilters().ToListAsync());
        Assert.Null(await app.Database.GetMetadataAsync(LocalDatabase.PullCursorKey));
        Assert.Null(await app.RawAsync<Note>(kept.Id));
    }

    [Fact]
    public async Task Timestamps_AreStoredExactly_AndSortChronologically()
    {
        Note older = await app.AddNoteAsync("Older");
        app.Time.Advance(TimeSpan.FromDays(400));
        Note newer = await app.AddNoteAsync("Newer");

        // EF cannot ORDER BY a DateTimeOffset on SQLite at all without the ticks converter.
        IReadOnlyList<Note> notes = await app.Notes.SearchAsync(null);

        Assert.Equal([newer.Id, older.Id], notes.Select(n => n.Id));
        Assert.Equal(app.Time.GetUtcNow(), (await app.RawAsync<Note>(newer.Id))!.UpdatedAt);
    }

    // ---- Mapper ----

    [Fact]
    public void Mapper_RoundTripsANote()
    {
        Note original = new()
        {
            Id = Guid.NewGuid(), Title = "Shopping", Body = "Milk\nBread",
            CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch.AddHours(1), ServerVersion = "v3"
        };

        Note copy = new();
        SyncRecordMapper.ApplyTo(Through(SyncRecordMapper.ToRecord(original)), copy);

        Assert.Equal(original.Id, copy.Id);
        Assert.Equal((original.Title, original.Body), (copy.Title, copy.Body));
        Assert.Equal((original.CreatedAt, original.UpdatedAt), (copy.CreatedAt, copy.UpdatedAt));
        Assert.Equal("v3", copy.ServerVersion);
        Assert.Equal(SyncStatus.Synced, copy.SyncStatus);
    }

    [Fact]
    public void Mapper_AnOmittedOptionalMember_ReadsAsNull()
    {
        SyncRecord record = new(SyncEntityKind.Note, Guid.NewGuid(), false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            "v1", JsonDocument.Parse("""{"title":"No body"}""").RootElement);

        Note note = new();
        SyncRecordMapper.ApplyTo(record, note);

        Assert.Equal("No body", note.Title);
        Assert.Null(note.Body);
    }

    [Fact]
    public void Mapper_ADeleteCarriesNoData_AndTheWireFormatUsesCamelCaseAndStringEnums()
    {
        SyncRecord tombstone = SyncRecordMapper.ToRecord(new Note { Id = Guid.NewGuid(), Title = "x", IsDeleted = true });
        SyncRecord live = SyncRecordMapper.ToRecord(new Note { Id = Guid.NewGuid(), Title = "x" });

        string json = JsonSerializer.Serialize(new PushRequest([tombstone, live]), SyncJsonContext.Default.PushRequest);

        Assert.Null(tombstone.Data);
        Assert.Contains("\"kind\":\"Note\"", json);
        Assert.Contains("\"title\":\"x\"", json);
        Assert.Contains("\"deleted\":true", json);
        Assert.DoesNotContain("\"body\"", json); // null members are omitted, not sent as null
    }

    private static SyncRecord Through(SyncRecord record) =>
        JsonSerializer.Deserialize(
            JsonSerializer.Serialize(record, SyncJsonContext.Default.SyncRecord), SyncJsonContext.Default.SyncRecord)!;
}
