using IdentityServerProject.Mobile.Core.Domain;

namespace IdentityServerProject.Mobile.Core.Services;

public interface INoteService
{
    /// <summary>Notes whose title or body contains <paramref name="term" />; all of them when blank. Most recently edited first.</summary>
    Task<IReadOnlyList<Note>> SearchAsync(string? term, CancellationToken cancellationToken = default);

    Task<Note?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates the note when <see cref="SyncEntity.Id" /> is empty, otherwise updates it. Returns the stored copy.</summary>
    /// <exception cref="System.ComponentModel.DataAnnotations.ValidationException">The note is not valid.</exception>
    Task<Note> SaveAsync(Note note, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
