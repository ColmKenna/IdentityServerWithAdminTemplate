using System.Text.Json;
using IdentityServerProject.Mobile.Core.Data;
using IdentityServerProject.Mobile.Core.Domain;
using IdentityServerProject.Mobile.Core.Sync;
using IdentityServerProject.Mobile.Core.Sync.Contract;
using IdentityServerProject.Mobile.Core.Tests.Infrastructure;

namespace IdentityServerProject.Mobile.Core.Tests.Sync;

public sealed class SyncEngineTests : IAsyncLifetime
{
    private readonly ClientHarness app = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await app.DisposeAsync();

    private static PushResponse Accept(PushRequest request, string version = "s1") =>
        new(request.Changes.Select(c => new PushResult(c.Kind, c.Id, PushOutcome.Accepted, version, null, null)).ToList());

    private static SyncRecord ServerNote(Guid id, string title, string version = "s5", bool deleted = false)
    {
        SyncRecord record = SyncRecordMapper.ToRecord(new Note
        {
            Id = id, Title = title, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch.AddDays(1),
            IsDeleted = deleted, ServerVersion = version
        });
        return record;
    }

    // ---- Push ----

    [Fact]
    public async Task Push_SendsEverythingPending_OldestEditFirst_AndMarksItSynced()
    {
        Note first = await app.AddNoteAsync("First");
        app.Time.Advance(TimeSpan.FromMinutes(1));
        Note second = await app.AddNoteAsync("Second");
        app.Api.OnPush = request => Accept(request, "s1");

        SyncRunResult result = await app.Engine.RunAsync(CancellationToken.None);

        PushRequest sent = Assert.Single(app.Api.Pushes);
        Assert.Equal([first.Id, second.Id], sent.Changes.Select(c => c.Id));
        Assert.All(sent.Changes, c => Assert.Equal(SyncEntityKind.Note, c.Kind));
        Assert.Equal(2, result.Accepted);

        foreach (Guid id in new[] { first.Id, second.Id })
        {
            Note stored = (await app.RawAsync<Note>(id))!;
            Assert.Equal(SyncStatus.Synced, stored.SyncStatus);
            Assert.Equal("s1", stored.ServerVersion);
        }
    }

    [Fact]
    public async Task Push_NothingPending_DoesNotCallThePushEndpoint()
    {
        await app.AddNoteAsync();
        await app.MarkEverythingSyncedAsync();

        await app.Engine.RunAsync(CancellationToken.None);

        Assert.Empty(app.Api.Pushes);
    }

    [Fact]
    public async Task Push_SendsTheVersionTheDeviceLastSaw_AndNoneForNewRecords()
    {
        Note known = await app.AddNoteAsync("Known");
        await app.MarkEverythingSyncedAsync("v7");
        Note edit = (await app.Notes.GetAsync(known.Id))!;
        edit.Title = "Known (edited)";
        await app.Notes.SaveAsync(edit);
        Note fresh = await app.AddNoteAsync("Fresh");

        await app.Engine.RunAsync(CancellationToken.None);

        IReadOnlyList<SyncRecord> changes = app.Api.Pushes.Single().Changes;
        Assert.Equal("v7", changes.Single(c => c.Id == known.Id).Version);
        Assert.Null(changes.Single(c => c.Id == fresh.Id).Version);
    }

    [Fact]
    public async Task Push_ADeleteTheServerAcknowledged_IsRemovedFromTheDevice()
    {
        Note note = await app.AddNoteAsync();
        await app.MarkEverythingSyncedAsync();
        await app.Notes.DeleteAsync(note.Id);
        app.Api.OnPush = request => Accept(request, "s2");

        await app.Engine.RunAsync(CancellationToken.None);

        SyncRecord sent = app.Api.Pushes.Single().Changes.Single();
        Assert.True(sent.Deleted);
        Assert.Null(sent.Data);
        Assert.Null(await app.RawAsync<Note>(note.Id));
    }

    [Fact]
    public async Task Push_ARecordEditedWhileTheRequestWasInFlight_IsNotMarkedSynced()
    {
        Note note = await app.AddNoteAsync("Original");
        app.Api.OnPush = request =>
        {
            // The user keeps typing while the phone is talking to the server.
            app.Time.Advance(TimeSpan.FromSeconds(10));
            Note edit = app.Notes.GetAsync(note.Id).GetAwaiter().GetResult()!;
            edit.Title = "Edited meanwhile";
            app.Notes.SaveAsync(edit).GetAwaiter().GetResult();
            return Accept(request, "s2");
        };

        await app.Engine.RunAsync(CancellationToken.None);

        Note stored = (await app.RawAsync<Note>(note.Id))!;
        Assert.Equal("Edited meanwhile", stored.Title);
        Assert.Equal(SyncStatus.Pending, stored.SyncStatus);
        Assert.Equal("s2", stored.ServerVersion); // the next push is based on what the server now holds
    }

    [Fact]
    public async Task Push_AConflict_ReplacesTheLocalCopyWithTheServers()
    {
        Note note = await app.AddNoteAsync("Mine");
        app.Api.OnPush = request => new PushResponse(
        [
            new PushResult(SyncEntityKind.Note, note.Id, PushOutcome.Conflict, null,
                ServerNote(note.Id, "Theirs", "s9"), null)
        ]);

        SyncRunResult result = await app.Engine.RunAsync(CancellationToken.None);

        Note stored = (await app.RawAsync<Note>(note.Id))!;
        Assert.Equal(1, result.Conflicts);
        Assert.Equal("Theirs", stored.Title);
        Assert.Equal("s9", stored.ServerVersion);
        Assert.Equal(SyncStatus.Synced, stored.SyncStatus);
    }

    [Fact]
    public async Task Push_AConflictOnARecordEditedAgainSince_KeepsTheNewerEditOnTheServersVersion()
    {
        Note note = await app.AddNoteAsync("First edit");
        app.Api.OnPush = request =>
        {
            app.Time.Advance(TimeSpan.FromSeconds(10));
            Note edit = app.Notes.GetAsync(note.Id).GetAwaiter().GetResult()!;
            edit.Title = "Second edit";
            app.Notes.SaveAsync(edit).GetAwaiter().GetResult();
            return new PushResponse(
            [
                new PushResult(SyncEntityKind.Note, note.Id, PushOutcome.Conflict, null,
                    ServerNote(note.Id, "Theirs", "s9"), null)
            ]);
        };

        SyncRunResult result = await app.Engine.RunAsync(CancellationToken.None);

        Note stored = (await app.RawAsync<Note>(note.Id))!;
        Assert.Equal(0, result.Conflicts);
        Assert.Equal("Second edit", stored.Title);
        Assert.Equal(SyncStatus.Pending, stored.SyncStatus);
        Assert.Equal("s9", stored.ServerVersion);
    }

    [Fact]
    public async Task Push_ARefusal_IsRecordedAndNotRetriedUntilTheUserEditsTheRecord()
    {
        Note note = await app.AddNoteAsync("Duplicate");
        app.Api.OnPush = request => new PushResponse(
        [
            new PushResult(SyncEntityKind.Note, note.Id, PushOutcome.Rejected, null, null, "Title already exists")
        ]);

        SyncRunResult first = await app.Engine.RunAsync(CancellationToken.None);
        await app.Engine.RunAsync(CancellationToken.None);

        Note stored = (await app.RawAsync<Note>(note.Id))!;
        Assert.Equal(1, first.Rejected);
        Assert.Equal("Title already exists", stored.SyncError);
        Assert.Equal(SyncStatus.Pending, stored.SyncStatus);
        Assert.Single(app.Api.Pushes); // the second run had nothing to send

        Note edit = (await app.Notes.GetAsync(note.Id))!;
        edit.Title = "Unique";
        await app.Notes.SaveAsync(edit);
        await app.Engine.RunAsync(CancellationToken.None);

        Assert.Equal(2, app.Api.Pushes.Count);
    }

    [Fact]
    public async Task Push_AServerThatAcknowledgesNothing_IsNotAskedAgainInALoop()
    {
        await app.AddNoteAsync();
        app.Api.OnPush = _ => new PushResponse([]);

        await app.Engine.RunAsync(CancellationToken.None);

        Assert.Single(app.Api.Pushes);
    }

    [Fact]
    public async Task Push_MoreThanOneBatchOfChanges_IsSentInBatches()
    {
        for (int i = 0; i < SyncEngine.PushBatchSize + 5; i++) await app.AddNoteAsync($"Note {i}");
        app.Api.OnPush = request => Accept(request);

        SyncRunResult result = await app.Engine.RunAsync(CancellationToken.None);

        Assert.Equal([SyncEngine.PushBatchSize, 5], app.Api.Pushes.Select(p => p.Changes.Count));
        Assert.Equal(SyncEngine.PushBatchSize + 5, result.Accepted);
    }

    // ---- Pull ----

    [Fact]
    public async Task Pull_StoresServerRecordsAsSynced_AndResumesFromTheCursorNextTime()
    {
        Guid noteId = Guid.NewGuid();
        app.Api.OnPull = (cursor, _) => cursor is null
            ? new PullResponse([ServerNote(noteId, "From server", "s5")], "cursor-1", false)
            : new PullResponse([], cursor, false);

        SyncRunResult first = await app.Engine.RunAsync(CancellationToken.None);
        await app.Engine.RunAsync(CancellationToken.None);

        Note stored = (await app.RawAsync<Note>(noteId))!;
        Assert.Equal(1, first.Pulled);
        Assert.Equal("From server", stored.Title);
        Assert.Equal(SyncStatus.Synced, stored.SyncStatus);
        Assert.Equal("s5", stored.ServerVersion);
        Assert.Equal([null, "cursor-1"], app.Api.Pulls.Select(p => p.Cursor));
        Assert.Equal("cursor-1", await app.Database.GetMetadataAsync(LocalDatabase.PullCursorKey));
    }

    [Fact]
    public async Task Pull_NeverOverwritesARecordWithUnpushedLocalEdits()
    {
        Note note = await app.AddNoteAsync("Mine");
        app.Api.OnPull = (_, _) => new PullResponse([ServerNote(note.Id, "Theirs", "s5")], "c", false);

        SyncRunResult result = await app.Engine.RunAsync(CancellationToken.None);

        Note stored = (await app.RawAsync<Note>(note.Id))!;
        Assert.Equal(0, result.Pulled);
        Assert.Equal("Mine", stored.Title);
        Assert.Equal(SyncStatus.Pending, stored.SyncStatus);
    }

    [Fact]
    public async Task Pull_ADeleteFromTheServer_RemovesTheLocalCopy()
    {
        Note note = await app.AddNoteAsync();
        await app.MarkEverythingSyncedAsync();
        app.Api.OnPull = (_, _) => new PullResponse([ServerNote(note.Id, "x", "s6", deleted: true)], "c", false);

        await app.Engine.RunAsync(CancellationToken.None);

        Assert.Null(await app.RawAsync<Note>(note.Id));
    }

    [Fact]
    public async Task Pull_ASecondPageIsRequestedWhileTheServerHasMore()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        app.Api.OnPull = (cursor, _) => cursor switch
        {
            null => new PullResponse([ServerNote(a, "A")], "page-1", true),
            "page-1" => new PullResponse([ServerNote(b, "B")], "page-2", false),
            _ => new PullResponse([], cursor, false)
        };

        SyncRunResult result = await app.Engine.RunAsync(CancellationToken.None);

        Assert.Equal(2, result.Pulled);
        Assert.Equal([null, "page-1"], app.Api.Pulls.Select(p => p.Cursor));
        Assert.Equal("page-2", await app.Database.GetMetadataAsync(LocalDatabase.PullCursorKey));
    }

    [Fact]
    public async Task Pull_ARecordSeenTwiceInOnePage_IsStoredOnceInItsLatestState()
    {
        Guid id = Guid.NewGuid();
        app.Api.OnPull = (_, _) => new PullResponse([ServerNote(id, "Old", "s1"), ServerNote(id, "New", "s2")], "c", false);

        await app.Engine.RunAsync(CancellationToken.None);

        Assert.Equal("New", (await app.RawAsync<Note>(id))!.Title);
    }

    [Fact]
    public async Task Pull_AnUnreadableRecord_IsSkipped_AndTheCursorStillAdvances()
    {
        Guid good = Guid.NewGuid(), bad = Guid.NewGuid();
        SyncRecord unreadable = ServerNote(bad, "ignored") with { Data = JsonDocument.Parse("""{"body":"no title"}""").RootElement };
        app.Api.OnPull = (_, _) => new PullResponse([unreadable, ServerNote(good, "Fine")], "c", false);

        SyncRunResult result = await app.Engine.RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Pulled);
        Assert.Null(await app.RawAsync<Note>(bad));
        Assert.Equal("Fine", (await app.RawAsync<Note>(good))!.Title);
        Assert.Equal("c", await app.Database.GetMetadataAsync(LocalDatabase.PullCursorKey));
    }

    // ---- Notifications ----

    [Fact]
    public async Task Run_AnnouncesASyncOriginChange_OnlyWhenSomethingChanged()
    {
        List<ChangeOrigin> raised = [];
        app.DataChanges.Changed += raised.Add;

        await app.Engine.RunAsync(CancellationToken.None);
        Assert.Empty(raised);

        app.Api.OnPull = (_, _) => new PullResponse([ServerNote(Guid.NewGuid(), "New")], "c", false);
        await app.Engine.RunAsync(CancellationToken.None);

        Assert.Equal([ChangeOrigin.Sync], raised);
    }

    [Fact]
    public async Task Run_WhenThePushFails_NothingIsMarkedSynced_AndThePullIsNotAttempted()
    {
        Note note = await app.AddNoteAsync();
        app.Api.OnPush = _ => throw new HttpRequestException("connection reset");

        await Assert.ThrowsAsync<HttpRequestException>(() => app.Engine.RunAsync(CancellationToken.None));

        Assert.Equal(SyncStatus.Pending, (await app.RawAsync<Note>(note.Id))!.SyncStatus);
        Assert.Empty(app.Api.Pulls);
    }
}
