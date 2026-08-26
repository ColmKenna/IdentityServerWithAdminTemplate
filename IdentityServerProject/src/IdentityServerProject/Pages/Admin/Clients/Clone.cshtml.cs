using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.SecretReveals;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.Clients;

public class CloneModel : PageModel
{
    private readonly IClientCreateService _clientCreateService;
    private readonly IClientDetailsService _clientDetailsService;
    private readonly ISecretRevealService _secretRevealService;

    public CloneModel(
        IClientCreateService clientCreateService,
        IClientDetailsService clientDetailsService,
        ISecretRevealService secretRevealService)
    {
        _clientCreateService = clientCreateService;
        _clientDetailsService = clientDetailsService;
        _secretRevealService = secretRevealService;
    }

    [BindProperty(SupportsGet = true)]
    public string SourceClientId { get; set; } = string.Empty;

    public string SourceClientName { get; set; } = string.Empty;

    [BindProperty]
    public ClientCreateInputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SourceClientId))
            return RedirectToPage("./Index");

        var sourceClient = await LoadSourceClientAsync(cancellationToken);
        if (sourceClient == null)
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

        var sourceClient = await LoadSourceClientAsync(cancellationToken);
        if (sourceClient == null)
            return NotFound();

        ModelState.Remove("Input.SelectedPreset");

        if (!ModelState.IsValid)
            return Page();

        var result = await _clientCreateService.CloneClientAsync(SourceClientId, Input, cancellationToken);
        if (!result.Success)
        {
            AddErrorsToModelState(result.Errors);

            if (result.Errors.Count == 0)
                ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Failed to clone client.");
            return Page();
        }

        if (!string.IsNullOrEmpty(result.PlaintextSecret))
        {
            var ticket = await _secretRevealService.IssueAsync(
                new SecretRevealTarget(SecretRevealPurpose.ClientCreated, result.ClientId!),
                result.PlaintextSecret,
                cancellationToken);
            TempData["SecretRevealHandle"] = ticket.Handle;
        }

        return RedirectToPage("./Create", new { clientId = result.ClientId });
    }

    private async Task<ClientDetailsModel?> LoadSourceClientAsync(CancellationToken cancellationToken)
    {
        var sourceClient = await _clientDetailsService.GetClientDetailsAsync(ClientId.Create(SourceClientId), cancellationToken);
        if (sourceClient != null)
            SourceClientName = sourceClient.ClientName ?? SourceClientId;

        return sourceClient;
    }

    private void AddErrorsToModelState(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
        {
            var key = string.IsNullOrEmpty(field) || field.StartsWith("Input.", StringComparison.Ordinal)
                ? field
                : $"Input.{field}";

            foreach (var message in messages)
            {
                ModelState.AddModelError(key, message);
            }
        }
    }
}
