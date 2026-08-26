using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.Apis;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.SecretReveals;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Pages.Admin.Apis;

public class EditorModel : PageModel
{
    private readonly IApiResourceEditorService _apiResourceEditorService;
    private readonly ISecretRevealService _secretRevealService;

    // Retained for direct handler tests; runtime activation is pinned to the DI constructor
    // below so a deployed app can never silently omit the reveal service.
    public EditorModel(IApiResourceEditorService apiResourceEditorService)
        : this(apiResourceEditorService, UnconfiguredSecretRevealService.Instance)
    {
    }

    [ActivatorUtilitiesConstructor]
    public EditorModel(
        IApiResourceEditorService apiResourceEditorService,
        ISecretRevealService secretRevealService)
    {
        _apiResourceEditorService = apiResourceEditorService;
        _secretRevealService = secretRevealService;
    }

    [BindProperty(SupportsGet = true)]
    public string? Name { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "basics";

    public ApiResourceEditorModel Editor { get; private set; } = ApiResourceEditorModel.Empty();

    public List<string> AttachableScopeNames { get; private set; } = new();

    public string? GeneratedSecret { get; set; }

    [TempData]
    public string? SecretRevealHandle { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [BindProperty]
    public BasicsInputModel Basics { get; set; } = new();

    [BindProperty]
    public SecretInputModel Secret { get; set; } = new();

    [BindProperty]
    public AttachScopeInputModel AttachScope { get; set; } = new();

    [BindProperty]
    public CreateScopeInputModel CreateScope { get; set; } = new();

    [BindProperty]
    public ClaimInputModel Claim { get; set; } = new();

    private bool HasResourceName => !string.IsNullOrWhiteSpace(Name);
    private string ResourceName => Name!;
    private ScopeName ResourceScopeName => ScopeName.Create(ResourceName);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken = default)
    {
        Tab = NormalizeTab(Tab);

        if (!HasResourceName)
            return InitializeApiResourceEditor();

        var editor = await _apiResourceEditorService.GetForEditAsync(ResourceScopeName, cancellationToken);
        if (editor == null)
            return NotFound();

        Editor = editor;
        Basics = new BasicsInputModel
        {
            Name = editor.Name,
            DisplayName = editor.DisplayName,
            Description = editor.Description,
        };

        if (Tab == "scopes")
            await LoadAttachableScopeNamesAsync(editor, cancellationToken);

        var handle = SecretRevealHandle;
        if (!string.IsNullOrEmpty(handle))
        {
            var reveal = await _secretRevealService.ConsumeAsync(
                new SecretRevealTarget(SecretRevealPurpose.ApiResourceSecretGenerated, ResourceName),
                IdentityServerProject.Services.SecretReveals.SecretRevealHandle.Create(handle),
                cancellationToken);
            if (reveal.Status == SecretRevealConsumeStatus.Revealed)
                GeneratedSecret = reveal.Plaintext;
        }

        return Page();
    }

    private IActionResult InitializeApiResourceEditor()
    {
        Editor = ApiResourceEditorModel.Empty();
        Tab = "basics";
        return Page();
    }

    public async Task<IActionResult> OnPostSaveBasicsAsync(CancellationToken cancellationToken = default)
    {
        var command = new SaveApiResourceBasicsCommand(
            Name != null ? ScopeName.Create(Name) : null,
            ScopeName.Create(Basics.Name),
            Basics.DisplayName,
            Basics.Description);
        var result = await _apiResourceEditorService.SaveBasicsAsync(command, cancellationToken);

        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        if (!result.Succeeded)
            return await RedisplayWithErrorsAsync(result, cancellationToken);

        return RedirectToPage(new { name = Basics.Name, tab = "basics" });
    }

    private async Task<IActionResult> RedisplayWithErrorsAsync(
        SaveApiResourceBasicsResult result,
        CancellationToken cancellationToken = default)
    {
        AddErrorsToModelState(result);
        await PopulateEditorAsync(cancellationToken);
        Tab = "basics";
        return Page();
    }

    private void AddErrorsToModelState(SaveApiResourceBasicsResult result)
    {
        if (result.ValidationErrors != null)
            result.ValidationErrors.AddToModelState(ModelState, "Basics");
        else
        {
            var errors = result.Errors.SelectMany(error => error.Value.Select(message => (error.Key, message)));

            foreach (var (key, message) in errors)
            {
                ModelState.AddModelError(key, message);
            }
        }
    }

    public async Task<IActionResult> OnPostAddSecretAsync(CancellationToken cancellationToken = default)
    {
        if (!HasResourceName)
            return NotFound();

        var command = new AddApiResourceSecretCommand(ResourceScopeName, Secret.Description, Secret.Expiration);
        var result = await _apiResourceEditorService.AddSecretAsync(command, cancellationToken);

        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        if (!result.Success)
            return await RedisplayWithErrorsAsync(result.Errors, "secrets", cancellationToken);

        var ticket = await _secretRevealService.IssueAsync(
            new SecretRevealTarget(SecretRevealPurpose.ApiResourceSecretGenerated, ResourceName),
            result.PlaintextSecret!,
            cancellationToken);
        SecretRevealHandle = ticket.Handle;
        return RedirectToEditorTab("secrets");
    }

    public async Task<IActionResult> OnPostRevokeSecretAsync(int secretId, string? confirmation = null, CancellationToken cancellationToken = default)
    {
        if (!HasResourceName)
            return NotFound();

        if (!HasConfirmation(confirmation, "REVOKE"))
            return RedirectToSecretsTab("Type REVOKE to confirm secret revocation.", "secrets");

        var result = await _apiResourceEditorService.RevokeSecretAsync(ResourceScopeName, secretId, cancellationToken);
        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        if (!result.Succeeded)
            return await RedisplayWithErrorsAsync(result.Errors, "secrets", cancellationToken);

        return RedirectToEditorTab("secrets");
    }

    private IActionResult RedirectToSecretsTab(string typeRevokeToConfirmSecretRevocation, string secrets)
    {
        ErrorMessage = typeRevokeToConfirmSecretRevocation;
        return RedirectToEditorTab(secrets);
    }

    public async Task<IActionResult> OnPostAttachScopeAsync(CancellationToken cancellationToken = default)
    {
        if (!HasResourceName)
            return NotFound();

        var result = await _apiResourceEditorService.AttachScopeAsync(ResourceScopeName,
            ScopeName.Create(AttachScope.ScopeName), cancellationToken);

        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        if (!result.Succeeded)
            return await RedisplayWithErrorsAsync(result.Errors, "scopes", cancellationToken);

        return RedirectToEditorTab("scopes");
    }

    public async Task<IActionResult> OnPostCreateScopeAsync(CancellationToken cancellationToken = default)
    {
        if (!HasResourceName)
            return NotFound();

        var command =
            new CreateApiResourceScopeCommand(ResourceScopeName, CreateScope.ScopeName, CreateScope.ScopeDisplayName);
        var result = await _apiResourceEditorService.CreateScopeAsync(command, cancellationToken);

        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        if (!result.Succeeded)
            return await RedisplayWithErrorsAsync(result.Errors, "scopes", cancellationToken);

        return RedirectToEditorTab("scopes");
    }

    public async Task<IActionResult> OnPostDetachScopeAsync(string scopeName,
        CancellationToken cancellationToken = default)
    {
        if (!HasResourceName)
            return NotFound();

        var result =
            await _apiResourceEditorService.DetachScopeAsync(ResourceScopeName, ScopeName.Create(scopeName),
                cancellationToken);
        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        if (!result.Succeeded)
            return await RedisplayWithErrorsAsync(result.Errors, "scopes", cancellationToken);

        return RedirectToEditorTab("scopes");
    }

    public async Task<IActionResult> OnPostAddClaimAsync(CancellationToken cancellationToken = default)
    {
        if (!HasResourceName)
            return NotFound();

        var command = new AddApiResourceClaimCommand(ResourceScopeName, ClaimType.Create(Claim.ClaimType));
        var result = await _apiResourceEditorService.AddClaimAsync(command, cancellationToken);

        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        if (!result.Succeeded)
            return await RedisplayWithErrorsAsync(result.Errors, "claims", cancellationToken);

        return RedirectToEditorTab("claims");
    }

    public async Task<IActionResult> OnPostRemoveClaimAsync(string claimType,
        CancellationToken cancellationToken = default)
    {
        if (!HasResourceName)
            return NotFound();

        var result = await _apiResourceEditorService
            .RemoveClaimAsync(ResourceScopeName, ClaimType.Create(claimType), cancellationToken);
        
        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        if (!result.Succeeded)
            return await RedisplayWithErrorsAsync(result.Errors, "claims", cancellationToken);

        return RedirectToEditorTab("claims");
    }

    public async Task<IActionResult> OnPostEnableAsync(CancellationToken cancellationToken = default)
    {
        if (!HasResourceName)
            return NotFound();

        var result =
            await _apiResourceEditorService.SetEnabledAsync(ResourceScopeName, enabled: true, cancellationToken);
        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        return RedirectToEditorTab("basics");
    }

    public async Task<IActionResult> OnPostDisableAsync(string? confirmation = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasResourceName)
            return NotFound();

        if (!HasConfirmation(confirmation, "DISABLE"))
            return RedirectToSecretsTab("Type DISABLE to confirm disabling this API resource.", "basics");

        var result =
            await _apiResourceEditorService.SetEnabledAsync(ResourceScopeName, enabled: false, cancellationToken);
        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        return RedirectToEditorTab("basics");
    }

    public async Task<IActionResult> OnPostDeleteAsync(string? confirmation = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasResourceName)
            return NotFound();

        if (!HasConfirmation(confirmation, "DELETE"))
            return RedirectToSecretsTab("Type DELETE to confirm deleting this API resource.", "basics");

        var result = await _apiResourceEditorService.DeleteAsync(ResourceScopeName, cancellationToken);
        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        return RedirectToPage("./Index");
    }

    private async Task PopulateEditorAsync(CancellationToken cancellationToken)
    {
        if (HasResourceName)
        {
            Editor = await _apiResourceEditorService.GetForEditAsync(ResourceScopeName, cancellationToken) ??
                     ApiResourceEditorModel.Empty();
            var allScopeNames = (await _apiResourceEditorService.GetAllApiScopeNamesAsync(cancellationToken)) ??
                                new List<string>();
            AttachableScopeNames = allScopeNames.Where(scopeName => !Editor.Scopes.Contains(scopeName)).ToList();
        }
        else
        {
            Editor = ApiResourceEditorModel.Empty();
        }
    }

    private async Task LoadAttachableScopeNamesAsync(ApiResourceEditorModel editor, CancellationToken cancellationToken)
    {
        var allScopeNames = (await _apiResourceEditorService.GetAllApiScopeNamesAsync(cancellationToken)) ??
                            new List<string>();
        AttachableScopeNames = allScopeNames.Where(s => !editor.Scopes.Contains(s)).ToList();
    }

    private async Task<IActionResult> RedisplayWithErrorsAsync(
        IReadOnlyDictionary<string, string[]> errors,
        string tab,
        CancellationToken cancellationToken)
    {
        foreach (var (key, messages) in errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(key, message);
            }
        }

        await PopulateEditorAsync(cancellationToken);
        Tab = tab;
        return Page();
    }

    private RedirectToPageResult RedirectToEditorTab(string tab) =>
        RedirectToPage(new { name = ResourceName, tab });

    private static string NormalizeTab(string? tab)
    {
        return tab?.ToLowerInvariant() switch
        {
            "secrets" => "secrets",
            "scopes" => "scopes",
            "claims" => "claims",
            _ => "basics",
        };
    }

    private static bool HasConfirmation(string? confirmation, string expected) =>
        string.Equals(confirmation?.Trim(), expected, StringComparison.Ordinal);

    public class BasicsInputModel
    {
        [StringLength(ValidationConstants.MaxNameLength)]
        public string Name { get; set; } = string.Empty;

        [StringLength(ValidationConstants.MaxDisplayNameLength)]
        public string? DisplayName { get; set; }

        [StringLength(ValidationConstants.MaxDescriptionLength)]
        public string? Description { get; set; }
    }

    public class SecretInputModel
    {
        [StringLength(ValidationConstants.MaxSecretDescriptionLength)]
        public string? Description { get; set; }

        public DateTime? Expiration { get; set; }
    }

    public class AttachScopeInputModel
    {
        public string ScopeName { get; set; } = string.Empty;
    }

    public class CreateScopeInputModel
    {
        [StringLength(ValidationConstants.MaxScopeNameLength)]
        public string ScopeName { get; set; } = string.Empty;

        [StringLength(ValidationConstants.MaxDisplayNameLength)]
        public string? ScopeDisplayName { get; set; }
    }

    public class ClaimInputModel
    {
        [StringLength(ValidationConstants.MaxClaimTypeLength)]
        public string ClaimType { get; set; } = string.Empty;
    }

    private sealed class UnconfiguredSecretRevealService : ISecretRevealService
    {
        public static UnconfiguredSecretRevealService Instance { get; } = new();

        public Task<SecretRevealTicket> IssueAsync(
            SecretRevealTarget target,
            string plaintext,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The secret reveal service is not configured.");

        public Task<SecretRevealConsumeResult> ConsumeAsync(
            SecretRevealTarget target,
            SecretRevealHandle handle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretRevealConsumeResult.Unavailable());
    }
}
