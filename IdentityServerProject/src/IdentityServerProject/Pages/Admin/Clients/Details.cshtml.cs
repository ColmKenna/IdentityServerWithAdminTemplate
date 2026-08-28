using IdentityServerProject.Services.Clients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.Clients;

public class DetailsModel(IClientDetailsService clientDetailsService) : PageModel
{
    private readonly IClientDetailsService _clientDetailsService = clientDetailsService;

    public ClientDetailsModel Client { get; private set; } = default!;

    [TempData] public string? DeleteBlockedMessage { get; set; }

    [BindProperty] public string? DeleteConfirmation { get; set; }

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        ClientDetailsModel? client =
            await _clientDetailsService.GetClientDetailsAsync(ClientId.Create(id), cancellationToken);
        if (client is null)
            return NotFound();

        Client = client;
        return Page();
    }

    public async Task<IActionResult> OnPostToggleStatusAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        bool success = await _clientDetailsService.ToggleClientStatusAsync(ClientId.Create(id), cancellationToken);
        if (!success)
            return NotFound();

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        if (!string.Equals(DeleteConfirmation?.Trim(), "DELETE", StringComparison.Ordinal))
        {
            DeleteBlockedMessage = "Type DELETE exactly to confirm permanent deletion.";
            return RedirectToPage(new { id });
        }

        ClientDeleteResult result =
            await _clientDetailsService.DeleteClientAsync(ClientId.Create(id), cancellationToken);
        if (!result.Success)
        {
            if (result.ErrorMessage == "Client not found.")
                return NotFound();

            DeleteBlockedMessage = result.ErrorMessage;
            return RedirectToPage(new { id });
        }

        return RedirectToPage("./Index");
    }
}