using System.ComponentModel.DataAnnotations;
using IdentityServerProject.Services.IdentityResources;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.IdentityResources;

public class EditInputModel
{
    [StringLength(200)]
    [Display(Name = "Display Name")]
    public string? DisplayName { get; set; }

    [StringLength(1000)] public string? Description { get; set; }

    [Display(Name = "Enabled")] public bool Enabled { get; set; }

    [Display(Name = "Required")] public bool Required { get; set; }

    [Display(Name = "Emphasize")] public bool Emphasize { get; set; }

    [Display(Name = "Show in Discovery Document")]
    public bool ShowInDiscoveryDocument { get; set; }
}

public class EditModel : PageModel
{
    private readonly IIdentityResourceEditorService _editorService;

    public EditModel(IIdentityResourceEditorService editorService)
    {
        _editorService = editorService;
    }

    [FromQuery] public string Name { get; set; } = string.Empty;

    [BindProperty] public EditInputModel Input { get; set; } = new();

    public IdentityResourceEditorModel Editor { get; private set; } = new()
    {
        Name = string.Empty,
        DisplayName = null,
        Description = null,
        Enabled = true,
        Required = false,
        Emphasize = false,
        ShowInDiscoveryDocument = true,
        UserClaims = new List<string>()
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Name))
            return NotFound();

        IdentityResourceEditorModel? editor =
            await _editorService.GetForEditAsync(ScopeName.Create(Name), cancellationToken);
        if (editor is null)
            return NotFound();

        Editor = editor;
        Input = CreateInputModel(editor);

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Name))
            return NotFound();

        if (!ModelState.IsValid)
        {
            IdentityResourceEditorModel? editor =
                await _editorService.GetForEditAsync(ScopeName.Create(Name), cancellationToken);
            if (editor is null)
                return NotFound();
            Editor = editor;
            return Page();
        }

        IdentityResourceEditResult result = await _editorService.UpdateBasicsAsync(
            new UpdateIdentityResourceBasicsCommand(
                ScopeName.Create(Name),
                Input.DisplayName,
                Input.Description,
                Input.Enabled,
                Input.Required,
                Input.Emphasize,
                Input.ShowInDiscoveryDocument),
            cancellationToken);

        return await RenderOutcomeAsync(result, Name, cancellationToken);
    }

    public async Task<IActionResult> OnPostAddClaimAsync(string name, string claimType,
        CancellationToken cancellationToken)
    {
        IdentityResourceEditResult result = await _editorService.AddClaimAsync(ScopeName.Create(name),
            ClaimType.Create(claimType), cancellationToken);
        return await RenderOutcomeAsync(result, name, cancellationToken);
    }

    public async Task<IActionResult> OnPostRemoveClaimAsync(string name, string claimType,
        CancellationToken cancellationToken)
    {
        IdentityResourceEditResult result = await _editorService.RemoveClaimAsync(ScopeName.Create(name),
            ClaimType.Create(claimType), cancellationToken);
        return await RenderOutcomeAsync(result, name, cancellationToken);
    }

    /// <summary>
    ///     Turns a service outcome into a response. A refused mutation re-renders the editor with the
    ///     reason in the validation summary; it is deliberately not a 404, which would tell the
    ///     operator the resource does not exist when in fact it is guarded.
    /// </summary>
    private async Task<IActionResult> RenderOutcomeAsync(
        IdentityResourceEditResult result, string name, CancellationToken cancellationToken)
    {
        switch (result.Outcome)
        {
            case IdentityResourceEditOutcome.Success:
                return RedirectToPage(new { name });

            case IdentityResourceEditOutcome.Protected:
                IdentityResourceEditorModel? editor =
                    await _editorService.GetForEditAsync(ScopeName.Create(name), cancellationToken);
                if (editor is null)
                    return NotFound();

                Editor = editor;
                Input = CreateInputModel(editor);
                ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "This change is not permitted.");
                return Page();

            default:
                return NotFound();
        }
    }

    private static EditInputModel CreateInputModel(IdentityResourceEditorModel editor) => new()
    {
        DisplayName = editor.DisplayName,
        Description = editor.Description,
        Enabled = editor.Enabled,
        Required = editor.Required,
        Emphasize = editor.Emphasize,
        ShowInDiscoveryDocument = editor.ShowInDiscoveryDocument
    };
}