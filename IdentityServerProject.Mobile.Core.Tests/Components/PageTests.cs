using Bunit;
using Microsoft.AspNetCore.Components;
using IdentityServerProject.Mobile.Core.Auth;
using IdentityServerProject.Mobile.Core.Components.Pages;
using IdentityServerProject.Mobile.Core.Domain;
using IdentityServerProject.Mobile.Core.Tests.Infrastructure;

namespace IdentityServerProject.Mobile.Core.Tests.Components;

public sealed class PageTests : ClientComponentContext
{
    // ---- Notes ----

    [Fact]
    public void Notes_WithNone_InvitesTheUserToAddOne()
    {
        IRenderedComponent<Notes> page = Render<Notes>();

        page.WaitForAssertion(() => Assert.Contains("No notes yet", page.Markup));
        Assert.Contains("href=\"notes/new\"", page.Markup);
    }

    [Fact]
    public async Task Notes_ListsTheMostRecentlyEditedFirst_AndTheSearchBoxNarrowsTheList()
    {
        await App.AddNoteAsync("Older", "Ferry to the harbour");
        App.Time.Advance(TimeSpan.FromMinutes(1));
        await App.AddNoteAsync("Newer");
        IRenderedComponent<Notes> page = Render<Notes>();
        page.WaitForAssertion(() => Assert.Equal(["Newer", "Older"], page.FindAll(".row-main strong").Select(e => e.TextContent)));

        page.Find("input.search").Input("harbour");

        page.WaitForAssertion(() => Assert.Equal(["Older"], page.FindAll(".row-main strong").Select(e => e.TextContent)));
    }

    [Fact]
    public async Task Notes_ASearchWithNoResults_SaysSoRatherThanOfferingToAddTheFirstNote()
    {
        await App.AddNoteAsync("Shopping");
        IRenderedComponent<Notes> page = Render<Notes>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".row")));

        page.Find("input.search").Input("zzz");

        page.WaitForAssertion(() => Assert.Contains("No matches", page.Markup));
    }

    [Fact]
    public async Task Notes_ReloadsWhenSomethingElseChangesTheData()
    {
        IRenderedComponent<Notes> page = Render<Notes>();
        page.WaitForAssertion(() => Assert.Contains("No notes yet", page.Markup));

        await App.AddNoteAsync("Added elsewhere");

        page.WaitForAssertion(() => Assert.Contains("Added elsewhere", page.Markup));
    }

    [Fact]
    public async Task Notes_MarksRecordsThatHaveNotSyncedYet()
    {
        await App.AddNoteAsync("Unsynced");
        IRenderedComponent<Notes> page = Render<Notes>();

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".pending-dot")));
    }

    [Fact]
    public async Task NoteEdit_SavingWithoutATitle_ShowsTheProblem_AndStoresNothing()
    {
        IRenderedComponent<NoteEdit> page = Render<NoteEdit>();

        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Enter a title.", page.Markup));
        Assert.Empty(await App.Notes.SearchAsync(null));
    }

    [Fact]
    public async Task NoteEdit_AValidForm_SavesLocally_AndReturnsToTheList()
    {
        IRenderedComponent<NoteEdit> page = Render<NoteEdit>();

        page.Find("input").Change("Shopping list");
        page.Find("textarea").Change("Milk");
        page.Find("form").Submit();

        await WaitUntilAsync(async () => (await App.Notes.SearchAsync(null)).Count == 1);
        Note saved = (await App.Notes.SearchAsync(null)).Single();
        Assert.Equal(("Shopping list", "Milk"), (saved.Title, saved.Body));
        Assert.Equal(SyncStatus.Pending, saved.SyncStatus);
        Assert.EndsWith("/notes", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task NoteEdit_DeleteNeedsConfirmation_ThenRemovesTheNote()
    {
        Note note = await App.AddNoteAsync("Doomed");
        IRenderedComponent<NoteEdit> page = Render<NoteEdit>(p => p.Add(c => c.Id, note.Id));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".btn-danger-outline")));

        page.Find(".btn-danger-outline").Click();
        Assert.NotNull(await App.Notes.GetAsync(note.Id)); // asking is not deleting
        page.Find(".btn-danger").Click();

        await WaitUntilAsync(async () => await App.Notes.GetAsync(note.Id) is null);
    }

    [Fact]
    public void NoteEdit_AnUnknownId_SaysItNoLongerExists()
    {
        IRenderedComponent<NoteEdit> page = Render<NoteEdit>(p => p.Add(c => c.Id, Guid.NewGuid()));

        page.WaitForAssertion(() => Assert.Contains("no longer exists", page.Markup));
    }

    // ---- Home ----

    [Fact]
    public async Task Home_GreetsTheUserByFirstName_AndShowsTheirRecentNotes()
    {
        await SignInAsync();
        await App.AddNoteAsync("Synced one");
        await App.MarkEverythingSyncedAsync();
        await App.AddNoteAsync("Still on the phone");

        IRenderedComponent<Home> page = Render<Home>();

        page.WaitForAssertion(() => Assert.Contains("Still on the phone", page.Markup));
        Assert.Contains("Good to see you, Dana", page.Markup);
        Assert.Contains("Synced one", page.Markup);
        Assert.Equal(["2", "1"], page.FindAll(".metric-value").Select(e => e.TextContent));
    }

    // ---- Sign-in ----

    [Fact]
    public async Task SignIn_Success_GoesHome()
    {
        App.Oidc.OnSignIn = () => AuthResult.Success(ClientHarness.NewTokens());
        IRenderedComponent<SignIn> page = Render<SignIn>();

        page.Find("button.btn-primary").Click();

        await WaitUntilAsync(() => Task.FromResult(App.Session.State == SessionState.SignedIn));
        Assert.True(new Uri(Services.GetRequiredService<NavigationManager>().Uri).AbsolutePath == "/");
    }

    [Fact]
    public void SignIn_WhenTheServerCannotBeReached_ExplainsAndStaysPut()
    {
        App.Oidc.OnSignIn = () => AuthResult.Failed(AuthFailure.Unreachable);
        IRenderedComponent<SignIn> page = Render<SignIn>();

        page.Find("button.btn-primary").Click();

        page.WaitForAssertion(() => Assert.Contains("Couldn't reach the sign-in server", page.Markup));
    }

    [Fact]
    public void SignIn_ClosingTheBrowser_IsNotAnError()
    {
        App.Oidc.OnSignIn = () => AuthResult.Failed(AuthFailure.Cancelled);
        IRenderedComponent<SignIn> page = Render<SignIn>();

        page.Find("button.btn-primary").Click();

        page.WaitForAssertion(() => Assert.Equal(1, App.Oidc.SignInCalls));
        Assert.Empty(page.FindAll(".form-error"));
        Assert.False(page.Find("button.btn-primary").HasAttribute("disabled"));
    }

    // ---- Settings ----

    [Fact]
    public async Task Settings_SigningOut_WarnsAboutUnsyncedChanges_ThenWipesTheDevice()
    {
        App.Oidc.OnSignIn = () => AuthResult.Success(ClientHarness.NewTokens());
        await App.Session.SignInAsync();
        await App.AddNoteAsync("Not yet synced");
        await App.Coordinator.SyncNowAsync(); // publishes the pending count
        IRenderedComponent<Settings> page = Render<Settings>();
        page.WaitForAssertion(() => Assert.Contains("haven't reached the server", page.Markup));

        page.Find(".btn-danger-outline").Click();
        page.Find(".btn-danger").Click();

        await WaitUntilAsync(() => Task.FromResult(App.Session.State == SessionState.SignedOut));
        Assert.Empty(await App.Notes.SearchAsync(null));
        Assert.EndsWith("/signin", Services.GetRequiredService<NavigationManager>().Uri);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        DateTime giveUp = DateTime.UtcNow.AddSeconds(5);
        while (!await condition())
        {
            if (DateTime.UtcNow > giveUp) throw new TimeoutException("The expected change never happened.");
            await Task.Delay(10);
        }
    }
}
