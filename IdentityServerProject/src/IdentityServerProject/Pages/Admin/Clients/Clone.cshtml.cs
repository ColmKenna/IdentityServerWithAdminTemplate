using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.SecretReveals;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.Clients;

public class CloneModel(
    IClientCreateService clientCreateService,
    IClientOverviewService clientOverviewService,
    ISecretRevealService secretRevealService) : PageModel
{
    private readonly IClientCreateService _clientCreateService = clientCreateService;
    private readonly IClientOverviewService _clientOverviewService = clientOverviewService;
    private readonly ISecretRevealService _secretRevealService = secretRevealService;

    [BindProperty(SupportsGet = true)] public string SourceClientId { get; set; } = string.Empty;

    public string SourceClientName { get; set; } = string.Empty;

    [BindProperty] public ClientCreateInputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SourceClientId))
            return RedirectToPage("./Index");

        ClientDetailsModel? sourceClient = await LoadSourceClientAsync(cancellationToken);
        if (sourceClient is null)
            return NotFound();

        Input.ClientId = $"{SourceClientId}-clone";
        Input.ClientName = $"{SourceClientName} (Clone)";
        Input.Description = sourceClient.Description;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SourceClientId))
            return RedirectToPage("./Index");

        ClientDetailsModel? sourceClient = await LoadSourceClientAsync(cancellationToken);
        if (sourceClient is null)
            return NotFound();

        ModelState.Remove("Input.SelectedPreset");

        if (!ModelState.IsValid)
            return Page();

        ClientCreateResult result =
            await _clientCreateService.CloneClientAsync(SourceClientId, Input, cancellationToken);
        if (!result.Success)
        {
            AddErrorsToModelState(result.Errors);

            if (result.Errors.Count == 0)
                ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Failed to clone client.");
            return Page();
        }

        if (!string.IsNullOrEmpty(result.PlaintextSecret))
        {
            SecretRevealTicket ticket = await _secretRevealService.IssueAsync(
                new SecretRevealTarget(SecretRevealPurpose.ClientCreated, result.ClientId!),
                result.PlaintextSecret,
                cancellationToken);
            TempData["SecretRevealHandle"] = ticket.Handle;
        }

        return RedirectToPage("./Create", new { clientId = result.ClientId });
    }

    private async Task<ClientDetailsModel?> LoadSourceClientAsync(CancellationToken cancellationToken)
    {
        ClientDetailsModel? sourceClient =
            await _clientOverviewService.GetClientDetailsAsync(ClientId.Create(SourceClientId), cancellationToken);
        if (sourceClient is not null)
            SourceClientName = sourceClient.ClientName ?? SourceClientId;

        return sourceClient;
    }

    private void AddErrorsToModelState(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach ((string field, string[] messages) in errors)
        {
            string key = string.IsNullOrEmpty(field) || field.StartsWith("Input.", StringComparison.Ordinal)
                ? field
                : $"Input.{field}";

            foreach (string message in messages) ModelState.AddModelError(key, message);
        }
    }
}