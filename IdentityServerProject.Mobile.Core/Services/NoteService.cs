using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using IdentityServerProject.Mobile.Core.Data;
using IdentityServerProject.Mobile.Core.Domain;

namespace IdentityServerProject.Mobile.Core.Services;

public sealed class NoteService(LocalDatabase database, LocalDataChanges changes, TimeProvider time) : INoteService
{
    public async Task<IReadOnlyList<Note>> SearchAsync(string? term, CancellationToken cancellationToken = default)
    {
        await using MobileDbContext context = await database.OpenAsync(cancellationToken);
        IQueryable<Note> query = context.Notes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(term))
        {
            string pattern = LikePattern.Contains(term.Trim());
            query = query.Where(n =>
                EF.Functions.Like(n.Title, pattern, LikePattern.Escape)
                || (n.Body != null && EF.Functions.Like(n.Body, pattern, LikePattern.Escape)));
        }

        return await query.OrderByDescending(n => n.UpdatedAt).ToListAsync(cancellationToken);
    }

    public async Task<Note?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using MobileDbContext context = await database.OpenAsync(cancellationToken);
        return await context.Notes.AsNoTracking().SingleOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public async Task<Note> SaveAsync(Note note, CancellationToken cancellationToken = default)
    {
        note.Title = note.Title?.Trim() ?? string.Empty;
        note.Body = RecordStamps.Clean(note.Body);
        RecordStamps.ValidateOrThrow(note);

        await using MobileDbContext context = await database.OpenAsync(cancellationToken);
        DateTimeOffset now = time.GetUtcNow();
        Note stored;

        if (note.Id == Guid.Empty)
        {
            stored = new Note();
            stored.CopyEditableFieldsFrom(note);
            RecordStamps.ForCreate(stored, now);
            context.Notes.Add(stored);
        }
        else
        {
            stored = await context.Notes.SingleOrDefaultAsync(n => n.Id == note.Id, cancellationToken)
                     ?? throw new ValidationException("This note no longer exists.");
            stored.CopyEditableFieldsFrom(note);
            RecordStamps.ForEdit(stored, now);
        }

        await context.SaveChangesAsync(cancellationToken);
        changes.Raise(ChangeOrigin.Local);
        return stored.Clone();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using MobileDbContext context = await database.OpenAsync(cancellationToken);
        Note? note = await context.Notes.SingleOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (note is null) return;

        RecordStamps.Delete(context.Notes, note, time.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        changes.Raise(ChangeOrigin.Local);
    }
}
