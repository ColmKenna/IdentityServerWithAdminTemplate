using System.ComponentModel.DataAnnotations;
using IdentityServerProject.Services.Apis;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.SecretReveals;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

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

    [BindProperty(SupportsGet = true)] public string? Name { get; set; }

    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = EditorTab.Basics;

    public ApiResourceEditorModel Editor { get; private set; } = ApiResourceEditorModel.Empty();

    public List<string> AttachableScopeNames { get; private set; } = new();

    public string? GeneratedSecret { get; set; }

    [TempData] public string? SecretRevealHandle { get; set; }

    [TempData] public string? ErrorMessage { get; set; }

    [BindProperty] public BasicsInputModel Basics { get; set; } = new();

    [BindProperty] public SecretInputModel Secret { get; set; } = new();

    [BindProperty] public AttachScopeInputModel AttachScope { get; set; } = new();

    [BindProperty] public CreateScopeInputModel CreateScope { get; set; } = new();

    [BindProperty] public ClaimInputModel Claim { get; set; } = new();

    private bool HasResourceName => !string.IsNullOrWhiteSpace(Name);
    private string ResourceName => Name!;
    private ScopeName ResourceScopeName => ScopeName.Create(ResourceName);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken = default)
    {
        Tab = NormalizeTab(Tab);

        if (!HasResourceName)
            return InitializeApiResourceEditor();

        ApiResourceEditorModel? editor =
            await _apiResourceEditorService.GetForEditAsync(ResourceScopeName, cancellationToken);
        if (editor == null)
            return NotFound();

        Editor = editor;
        Basics = new BasicsInputModel
        {
            Name = editor.Name,
            DisplayName = editor.DisplayName,
            Description = editor.Description
        };

        if (Tab == EditorTab.Scopes)
            await LoadAttachableScopeNamesAsync(editor.Scopes, cancellationToken);

        string? handle = SecretRevealHandle;
        if (!string.IsNullOrEmpty(handle))
        {
            SecretRevealConsumeResult reveal = await _secretRevealService.ConsumeAsync(
                new SecretRevealTarget(SecretRevealPurpose.ApiResourceSecretGenerated, ResourceName),
                Services.SecretReveals.SecretRevealHandle.Create(handle),
                cancellationToken);
            if (reveal.Status == SecretRevealConsumeStatus.Revealed)
                GeneratedSecret = reveal.Plaintext;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveBasicsAsync(CancellationToken cancellationToken = default)
    {
        var command = new SaveApiResourceBasicsCommand(
            Name != null ? ScopeName.Create(Name) : null,
            ScopeName.Create(Basics.Name),
            Basics.DisplayName,
            Basics.Description);
        SaveApiResourceBasicsResult
            result = await _apiResourceEditorService.SaveBasicsAsync(command, cancellationToken);

        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        if (!result.Succeeded)
            return await RedisplayWithErrorsAsync(result, EditorTab.Basics, cancellationToken);

        return RedirectToPage(new { name = Basics.Name, tab = EditorTab.Basics });
    }

    public async Task<IActionResult> OnPostAddSecretAsync(CancellationToken cancellationToken = default)
    {
        if (!HasResourceName)
            return NotFound();

        var command = new AddApiResourceSecretCommand(ResourceScopeName, Secret.Description, Secret.Expiration);
        ApiResourceAddSecretResult result = await _apiResourceEditorService.AddSecretAsync(command, cancellationToken);

        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        if (!result.Success)
            return await RedisplayWithErrorsAsync(result.Errors, EditorTab.Secrets, cancellationToken);

        SecretRevealTicket ticket = await _secretRevealService.IssueAsync(
            new SecretRevealTarget(SecretRevealPurpose.ApiResourceSecretGenerated, ResourceName),
            result.PlaintextSecret!,
            cancellationToken);
        SecretRevealHandle = ticket.Handle;
        return RedirectToEditorTab(EditorTab.Secrets);
    }

    public async Task<IActionResult> OnPostRevokeSecretAsync(int secretId, string? confirmation = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasResourceName)
            return NotFound();

        if (!HasConfirmation(confirmation, ConfirmationWord.Revoke))
            return RedirectToTabWithError(EditorTab.Secrets, "Type REVOKE to confirm secret revocation.");

        AdminMutationResult result =
            await _apiResourceEditorService.RevokeSecretAsync(ResourceScopeName, secretId, cancellationToken);
        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        if (!result.Succeeded)
            return await RedisplayWithErrorsAsync(result.Errors, EditorTab.Secrets, cancellationToken);

        return RedirectToEditorTab(EditorTab.Secrets);
    }

    public Task<IActionResult> OnPostAttachScopeAsync(CancellationToken cancellationToken = default) =>
        ExecuteEditorMutationAsync(
            () => _apiResourceEditorService.AttachScopeAsync(ResourceScopeName,
                ScopeName.Create(AttachScope.ScopeName), cancellationToken),
            EditorTab.Scopes,
            cancellationToken);

    public Task<IActionResult> OnPostCreateScopeAsync(CancellationToken cancellationToken = default) =>
        ExecuteEditorMutationAsync(
            () => _apiResourceEditorService.CreateScopeAsync(
                new CreateApiResourceScopeCommand(ResourceScopeName, CreateScope.ScopeName,
                    CreateScope.ScopeDisplayName),
                cancellationToken),
            EditorTab.Scopes,
            cancellationToken);

    public Task<IActionResult> OnPostDetachScopeAsync(string scopeName,
        CancellationToken cancellationToken = default) =>
        ExecuteEditorMutationAsync(
            () => _apiResourceEditorService.DetachScopeAsync(ResourceScopeName, ScopeName.Create(scopeName),
                cancellationToken),
            EditorTab.Scopes,
            cancellationToken);

    public Task<IActionResult> OnPostAddClaimAsync(CancellationToken cancellationToken = default) =>
        ExecuteEditorMutationAsync(
            () => _apiResourceEditorService.AddClaimAsync(
                new AddApiResourceClaimCommand(ResourceScopeName, ClaimType.Create(Claim.ClaimType)),
                cancellationToken),
            EditorTab.Claims,
            cancellationToken);

    public Task<IActionResult> OnPostRemoveClaimAsync(string claimType,
        CancellationToken cancellationToken = default) =>
        ExecuteEditorMutationAsync(
            () => _apiResourceEditorService.RemoveClaimAsync(ResourceScopeName, ClaimType.Create(claimType),
                cancellationToken),
            EditorTab.Claims,
            cancellationToken);

    public async Task<IActionResult> OnPostEnableAsync(CancellationToken cancellationToken = default)
    {
        if (!HasResourceName)
            return NotFound();

        AdminMutationResult result =
            await _apiResourceEditorService.SetEnabledAsync(ResourceScopeName, true, cancellationToken);
        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        return RedirectToEditorTab(EditorTab.Basics);
    }

    public async Task<IActionResult> OnPostDisableAsync(string? confirmation = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasResourceName)
            return NotFound();

        if (!HasConfirmation(confirmation, ConfirmationWord.Disable))
            return RedirectToTabWithError(EditorTab.Basics, "Type DISABLE to confirm disabling this API resource.");

        AdminMutationResult result =
            await _apiResourceEditorService.SetEnabledAsync(ResourceScopeName, false, cancellationToken);
        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        return RedirectToEditorTab(EditorTab.Basics);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string? confirmation = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasResourceName)
            return NotFound();

        if (!HasConfirmation(confirmation, ConfirmationWord.Delete))
            return RedirectToTabWithError(EditorTab.Basics, "Type DELETE to confirm deleting this API resource.");

        AdminMutationResult result = await _apiResourceEditorService.DeleteAsync(ResourceScopeName, cancellationToken);
        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        return RedirectToPage("./Index");
    }

    private IActionResult InitializeApiResourceEditor()
    {
        Editor = ApiResourceEditorModel.Empty();
        Tab = EditorTab.Basics;
        return Page();
    }

    private async Task<IActionResult> RedisplayWithErrorsAsync(
        SaveApiResourceBasicsResult result,
        string tab,
        CancellationToken cancellationToken = default)
    {
        AddErrorsToModelState(result);
        await PopulateEditorAsync(cancellationToken);
        Tab = tab;
        return Page();
    }

    private void AddErrorsToModelState(SaveApiResourceBasicsResult result)
    {
        if (result.ValidationErrors != null)
            result.ValidationErrors.AddToModelState(ModelState);
        else
        {
            IEnumerable<(string Key, string message)> errors =
                result.Errors.SelectMany(error => error.Value.Select(message => (error.Key, message)));

            foreach ((string key, string message) in errors) ModelState.AddModelError(key, message);
        }
    }

    private IActionResult RedirectToTabWithError(string tab, string message)
    {
        ErrorMessage = message;
        return RedirectToEditorTab(tab);
    }

    private async Task PopulateEditorAsync(CancellationToken cancellationToken)
    {
        if (HasResourceName)
        {
            Editor = await _apiResourceEditorService.GetForEditAsync(ResourceScopeName, cancellationToken) ??
                     ApiResourceEditorModel.Empty();
            await LoadAttachableScopeNamesAsync(Editor.Scopes, cancellationToken);
        }
        else
            Editor = ApiResourceEditorModel.Empty();
    }

    private async Task LoadAttachableScopeNamesAsync(
        IEnumerable<string> attachedScopeNames,
        CancellationToken cancellationToken)
    {
        List<string> allScopeNames = await _apiResourceEditorService.GetAllApiScopeNamesAsync(cancellationToken) ??
                                     new List<string>();
        AttachableScopeNames = allScopeNames.Where(scopeName => !attachedScopeNames.Contains(scopeName)).ToList();
    }

    private async Task<IActionResult> RedisplayWithErrorsAsync(
        IReadOnlyDictionary<string, string[]> errors,
        string tab,
        CancellationToken cancellationToken)
    {
        foreach ((string key, string[] messages) in errors)
        foreach (string message in messages)
            ModelState.AddModelError(key, message);

        await PopulateEditorAsync(cancellationToken);
        Tab = tab;
        return Page();
    }

    private async Task<IActionResult> ExecuteEditorMutationAsync(
        Func<Task<AdminMutationResult>> mutation,
        string tab,
        CancellationToken cancellationToken)
    {
        if (!HasResourceName)
            return NotFound();

        AdminMutationResult result = await mutation();

        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        if (!result.Succeeded)
            return await RedisplayWithErrorsAsync(result.Errors, tab, cancellationToken);

        return RedirectToEditorTab(tab);
    }

    private RedirectToPageResult RedirectToEditorTab(string tab) =>
        RedirectToPage(new { name = ResourceName, tab });

    private static class EditorTab
    {
        public const string Basics = "basics";
        public const string Secrets = "secrets";
        public const string Scopes = "scopes";
        public const string Claims = "claims";
    }

    private static class ConfirmationWord
    {
        public const string Revoke = "REVOKE";
        public const string Disable = "DISABLE";
        public const string Delete = "DELETE";
    }

    private static string NormalizeTab(string? tab) =>
        tab?.ToLowerInvariant() switch
        {
            EditorTab.Secrets => EditorTab.Secrets,
            EditorTab.Scopes => EditorTab.Scopes,
            EditorTab.Claims => EditorTab.Claims,
            _ => EditorTab.Basics
        };

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