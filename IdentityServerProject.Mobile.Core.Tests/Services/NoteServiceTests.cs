using System.ComponentModel.DataAnnotations;
using IdentityServerProject.Mobile.Core.Data;
using IdentityServerProject.Mobile.Core.Domain;
using IdentityServerProject.Mobile.Core.Tests.Infrastructure;

namespace IdentityServerProject.Mobile.Core.Tests.Services;

public sealed class NoteServiceTests : IAsyncLifetime
{
    private readonly ClientHarness app = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await app.DisposeAsync();

    [Fact]
    public async Task Save_NewNote_GetsAnIdAndIsQueuedForSync()
    {
        Note saved = await app.Notes.SaveAsync(new Note { Title = "Shopping list" });

        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal(SyncStatus.Pending, saved.SyncStatus);
        Assert.Null(saved.ServerVersion);
        Assert.Equal(app.Time.GetUtcNow(), saved.CreatedAt);
        Assert.Equal("Shopping list", (await app.Notes.GetAsync(saved.Id))!.Title);
    }

    [Fact]
    public async Task Save_TrimsTextAndStoresABlankBodyAsNull()
    {
        Note saved = await app.Notes.SaveAsync(new Note { Title = "  Shopping list  ", Body = "   " });

        Assert.Equal("Shopping list", saved.Title);
        Assert.Null(saved.Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Save_WithoutATitle_IsRefused(string title)
    {
        await Assert.ThrowsAsync<ValidationException>(() => app.Notes.SaveAsync(new Note { Title = title }));

        Assert.Empty(await app.Notes.SearchAsync(null));
    }

    [Fact]
    public async Task Save_ATitleThatIsTooLong_IsRefused()
    {
        await Assert.ThrowsAsync<ValidationException>(() => app.Notes.SaveAsync(new Note { Title = new string('x', 201) }));
    }

    [Fact]
    public async Task Save_Existing_UpdatesTheRecordAndRequeuesIt()
    {
        Note created = await app.AddNoteAsync("Shopping list");
        await app.MarkEverythingSyncedAsync("v1");
        app.Time.Advance(TimeSpan.FromMinutes(5));

        Note edit = (await app.Notes.GetAsync(created.Id))!;
        edit.Title = "Weekly shop";
        await app.Notes.SaveAsync(edit);

        Note stored = (await app.Notes.GetAsync(created.Id))!;
        Assert.Equal("Weekly shop", stored.Title);
        Assert.Equal(SyncStatus.Pending, stored.SyncStatus);
        Assert.Equal("v1", stored.ServerVersion);
        Assert.Equal(created.CreatedAt, stored.CreatedAt);
        Assert.Equal(app.Time.GetUtcNow(), stored.UpdatedAt);
    }

    [Fact]
    public async Task Save_AnEditClearsTheServersPreviousRefusal()
    {
        Note created = await app.AddNoteAsync();
        await using (MobileDbContext context = await app.Database.OpenAsync())
        {
            (await context.Notes.SingleAsync()).SyncError = "Title already exists";
            await context.SaveChangesAsync();
        }

        Note edit = (await app.Notes.GetAsync(created.Id))!;
        edit.Title = "Different title";
        await app.Notes.SaveAsync(edit);

        Assert.Null((await app.Notes.GetAsync(created.Id))!.SyncError);
    }

    [Fact]
    public async Task Save_OfANoteThatNoLongerExists_IsRefused()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            app.Notes.SaveAsync(new Note { Id = Guid.NewGuid(), Title = "Ghost" }));
    }

    [Fact]
    public async Task Search_MatchesTitleAndBody_IgnoringCase_MostRecentlyEditedFirst()
    {
        await app.AddNoteAsync("Harbour trip", "Bring a coat");
        app.Time.Advance(TimeSpan.FromMinutes(1));
        await app.AddNoteAsync("Packing", "Tickets for the HARBOUR ferry");
        app.Time.Advance(TimeSpan.FromMinutes(1));
        await app.AddNoteAsync("Unrelated");

        Assert.Equal(["Unrelated", "Packing", "Harbour trip"], (await app.Notes.SearchAsync(null)).Select(n => n.Title));
        Assert.Equal(["Packing", "Harbour trip"], (await app.Notes.SearchAsync("harbour")).Select(n => n.Title));
    }

    [Fact]
    public async Task Search_TreatsPercentAndUnderscoreLiterally()
    {
        await app.AddNoteAsync("100% done");
        await app.AddNoteAsync("Plain title");

        Assert.Equal(["100% done"], (await app.Notes.SearchAsync("%")).Select(n => n.Title));
        Assert.Empty(await app.Notes.SearchAsync("_"));
    }

    [Fact]
    public async Task Delete_ANoteTheServerNeverSaw_RemovesIt()
    {
        Note note = await app.AddNoteAsync();
        Note other = await app.AddNoteAsync("Other");

        await app.Notes.DeleteAsync(note.Id);

        Assert.Null(await app.RawAsync<Note>(note.Id));
        Assert.NotNull(await app.RawAsync<Note>(other.Id));
    }

    [Fact]
    public async Task Delete_ANoteTheServerKnows_LeavesATombstoneToReportLater()
    {
        Note note = await app.AddNoteAsync();
        await app.MarkEverythingSyncedAsync();

        await app.Notes.DeleteAsync(note.Id);

        Note tombstone = (await app.RawAsync<Note>(note.Id))!;
        Assert.True(tombstone.IsDeleted);
        Assert.Equal(SyncStatus.Pending, tombstone.SyncStatus);
        Assert.Empty(await app.Notes.SearchAsync(null));
        Assert.Null(await app.Notes.GetAsync(note.Id));
    }

    [Fact]
    public async Task EveryEdit_AnnouncesALocalChange()
    {
        List<ChangeOrigin> raised = [];
        app.DataChanges.Changed += raised.Add;

        Note note = await app.AddNoteAsync();
        await app.Notes.DeleteAsync(note.Id);

        Assert.Equal([ChangeOrigin.Local, ChangeOrigin.Local], raised);
    }
}
