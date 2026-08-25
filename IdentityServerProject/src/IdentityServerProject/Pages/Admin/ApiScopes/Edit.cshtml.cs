using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.ApiScopes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.ApiScopes;

public class EditInputModel
{
    [StringLength(200)]
    [Display(Name = "Display Name")]
    public string? DisplayName { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    public bool Enabled { get; set; } = true;

    public bool Required { get; set; }

    public bool Emphasize { get; set; }

    [Display(Name = "Show in Discovery Document")]
    public bool ShowInDiscoveryDocument { get; set; } = true;
}

public class EditModel : PageModel
{
    private readonly IApiScopeEditorService _apiScopeEditorService;

    public EditModel(IApiScopeEditorService apiScopeEditorService)
    {
        _apiScopeEditorService = apiScopeEditorService;
    }

    // Bound from the query string only (never form body) so a tampered hidden/posted
    // field can never redirect a save/claim edit onto a different scope's row.
    [FromQuery]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    public EditInputModel Input { get; set; } = new();

    public ApiScopeEditorModel Editor { get; private set; } = new()
    {
        Name = string.Empty,
        DisplayName = null,
        Description = null,
        Claims = new(),
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return NotFound();
        }

        var editor = await _apiScopeEditorService.GetForEditAsync(Name, cancellationToken);
        if (editor == null)
        {
            return NotFound();
        }

        Editor = editor;
        Input = new EditInputModel
        {
            DisplayName = editor.DisplayName,
            Description = editor.Description,
            Enabled = editor.Enabled,
            Required = editor.Required,
            Emphasize = editor.Emphasize,
            ShowInDiscoveryDocument = editor.ShowInDiscoveryDocument,
        };

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            var editor = await _apiScopeEditorService.GetForEditAsync(Name, cancellationToken);
            if (editor == null)
            {
                return NotFound();
            }
            Editor = editor;
            return Page();
        }

        var success = await _apiScopeEditorService.UpdateBasicsAsync(
            new UpdateApiScopeBasicsCommand(
                IdentityServerProject.Services.Scopes.ScopeName.Create(Name),
                Input.DisplayName,
                Input.Description,
                Input.Enabled,
                Input.Required,
                Input.Emphasize,
                Input.ShowInDiscoveryDocument),
            cancellationToken);
        if (!success)
        {
            return NotFound();
        }

        return RedirectToPage(new { name = Name });
    }

    public async Task<IActionResult> OnPostAddClaimAsync(string name, string claimType, CancellationToken cancellationToken)
    {
        var success = await _apiScopeEditorService.AddClaimAsync(name, claimType, cancellationToken);
        if (!success)
        {
            return NotFound();
        }

        return RedirectToPage(new { name });
    }

    public async Task<IActionResult> OnPostRemoveClaimAsync(string name, string claimType, CancellationToken cancellationToken)
    {
        var success = await _apiScopeEditorService.RemoveClaimAsync(name, claimType, cancellationToken);
        if (!success)
        {
            return NotFound();
        }

        return RedirectToPage(new { name });
    }
}
