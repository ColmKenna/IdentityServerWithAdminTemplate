using System.ComponentModel.DataAnnotations;
using IdentityServerProject.Pages.Shared;
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

public class EditModel(IIdentityResourceEditorService editorService) : PageModel
{
    private readonly IIdentityResourceEditorService _editorService = editorService;

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
        UserClaims = []
    };

    /// <summary>
    ///     The claim chips for the editor, each already told whether it is locked and why.
    /// </summary>
    /// <remarks>
    ///     Which claims a resource may not lose is policy rather than presentation, so the view
    ///     renders this instead of deciding it. The reasons come from
    ///     <see cref="BuiltInIdentityResourcePolicy" />, which is also what the service quotes when it
    ///     refuses, so the lock text and the refusal message cannot drift apart.
    ///     <para>
    ///         This only shapes the UI. <c>IdentityResourceEditorService.RemoveClaimCoreAsync</c>
    ///         applies the same three rules in the same order, so a chip rendered without a remove
    ///         button is an affordance, not the guard.
    ///     </para>
    /// </remarks>
    public IReadOnlyList<ScopeChipItem> ClaimChips => BuildClaimChips(Editor);

    /// <summary>
    ///     "openid" is a scope, never a user claim, so it can never be removed from any resource.
    ///     The authoritative copy is <c>IdentityResourceEditorService.OpenIdClaimType</c>, which is
    ///     private; this mirrors it for display only and the service still decides.
    /// </summary>
    private const string OpenIdClaimType = "openid";

    private static IReadOnlyList<ScopeChipItem> BuildClaimChips(IdentityResourceEditorModel editor) =>
        editor.UserClaims
            .Select(claim => new ScopeChipItem(claim, IsLocked(editor, claim), LockReason(editor, claim)))
            .ToList();

    private static bool IsLocked(IdentityResourceEditorModel editor, string claim) =>
        editor.IsProtected
        || string.Equals(claim, OpenIdClaimType, StringComparison.OrdinalIgnoreCase)
        || BuiltInIdentityResourcePolicy.IsInvariantClaim(editor.Name, claim);

    private static string LockReason(IdentityResourceEditorModel editor, string claim)
    {
        // Ordered so the operator gets the specific reason before the general one, matching the
        // order the service checks them in.
        if (BuiltInIdentityResourcePolicy.IsInvariantClaim(editor.Name, claim))
            return BuiltInIdentityResourcePolicy.InvariantClaimMessage(editor.Name, claim);

        if (string.Equals(claim, OpenIdClaimType, StringComparison.OrdinalIgnoreCase))
            return $"The '{OpenIdClaimType}' claim is a scope rather than a user claim and cannot be removed.";

        return BuiltInIdentityResourcePolicy.ProtectedMessage(editor.Name);
    }

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