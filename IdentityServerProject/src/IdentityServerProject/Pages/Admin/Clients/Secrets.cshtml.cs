using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.SecretReveals;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.Clients;

public class SecretsModel : PageModel
{
    private readonly IClientDetailsService _clientDetailsService;
    private readonly ISecretRevealService _secretRevealService;

    public SecretsModel(
        IClientDetailsService clientDetailsService,
        ISecretRevealService secretRevealService)
    {
        _clientDetailsService = clientDetailsService;
        _secretRevealService = secretRevealService;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    [BindProperty]
    [StringLength(ValidationConstants.MaxClientSecretDescriptionLength)]
    public string? Description { get; set; }

    [BindProperty]
    [Display(Name = "Expiration Date")]
    public DateTime? Expiration { get; set; }

    public string? GeneratedSecret { get; set; }

    [TempData]
    public string? SecretRevealHandle { get; set; }

    [TempData]
    public string? RevokeErrorMessage { get; set; }

    public ClientSecretsModel Client { get; private set; } = default!;

    public bool RevealMode => !string.IsNullOrEmpty(GeneratedSecret);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return NotFound();
        }

        var handle = SecretRevealHandle;
        if (!string.IsNullOrEmpty(handle))
        {
            var reveal = await _secretRevealService.ConsumeAsync(
                SecretRevealPurpose.ClientSecretGenerated, Id, handle, cancellationToken);
            if (reveal.Status == SecretRevealConsumeStatus.Revealed)
            {
                GeneratedSecret = reveal.Plaintext;
            }
        }

        var secrets = await _clientDetailsService.GetClientSecretsAsync(Id, cancellationToken);
        if (secrets == null)
        {
            return NotFound();
        }

        Client = secrets;
        return Page();
    }

    public async Task<IActionResult> OnPostGenerateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return await LoadPageAsync(cancellationToken);
        }

        var result = await _clientDetailsService.GenerateClientSecretAsync(Id, Description, Expiration, cancellationToken);
        if (!result.Success)
        {
            if (result.Status == AdminMutationStatus.NotFound)
            {
                return NotFound();
            }

            foreach (var (field, messages) in result.Errors)
            {
                foreach (var message in messages)
                {
                    ModelState.AddModelError(field, message);
                }
            }

            return await LoadPageAsync(cancellationToken);
        }

        if (!string.IsNullOrEmpty(result.PlaintextSecret))
        {
            var ticket = await _secretRevealService.IssueAsync(
                SecretRevealPurpose.ClientSecretGenerated,
                Id,
                result.PlaintextSecret,
                cancellationToken);
            SecretRevealHandle = ticket.Handle;
        }

        return RedirectToPage(new { id = Id });
    }

    private async Task<IActionResult> LoadPageAsync(CancellationToken cancellationToken)
    {
        var secrets = await _clientDetailsService.GetClientSecretsAsync(Id, cancellationToken);
        if (secrets == null)
        {
            return NotFound();
        }

        Client = secrets;
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(int secretId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return NotFound();
        }

        var result = await _clientDetailsService.RevokeClientSecretAsync(Id, secretId, cancellationToken);
        if (!result.Success)
        {
            if (result.ErrorMessage is "Client not found." or "Secret not found.")
            {
                return NotFound();
            }

            RevokeErrorMessage = result.ErrorMessage;
        }

        return RedirectToPage(new { id = Id });
    }
}
