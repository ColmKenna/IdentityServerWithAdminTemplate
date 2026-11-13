using System.ComponentModel.DataAnnotations;

namespace IdentityServerProject.Mobile.Core.Domain;

/// <summary>
///     The sample record the template ships with. Replace it with your own entities; docs/mobile/README.md
///     lists every place a new synced entity has to be added.
/// </summary>
public sealed class Note : SyncEntity
{
    [Required(ErrorMessage = "Enter a title.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [StringLength(4000)] public string? Body { get; set; }

    public void CopyEditableFieldsFrom(Note other)
    {
        Title = other.Title;
        Body = other.Body;
    }

    public Note Clone() => (Note)MemberwiseClone();
}
