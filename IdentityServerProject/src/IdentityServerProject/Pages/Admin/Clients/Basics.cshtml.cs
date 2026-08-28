using System.ComponentModel.DataAnnotations;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.Clients;

public class BasicsInputModel
{
    [Required(ErrorMessage = "Client Name is required")]
    [StringLength(ValidationConstants.MaxNameLength)]
    [Display(Name = "Client Name")]
    public string ClientName { get; set; } = string.Empty;

    [Display(Name = "Description")]
    [StringLength(ValidationConstants.MaxDescriptionLength)]
    public string? Description { get; set; }
}

public class BasicsModel(IClientDetailsService clientDetailsService) : PageModel
{
    private readonly IClientDetailsService _clientDetailsService = clientDetailsService;

    [BindProperty(SupportsGet = true)] public string Id { get; set; } = string.Empty;

    [BindProperty] public BasicsInputModel Input { get; set; } = new();

    public string ClientIdDisplay { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
            return NotFound();

        ClientDetailsModel? client =
            await _clientDetailsService.GetClientDetailsAsync(ClientId.Create(Id), cancellationToken);
        if (client is null)
            return NotFound();

        ClientIdDisplay = client.ClientId;
        Input.ClientName = client.ClientName;
        Input.Description = client.Description;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
            return NotFound();

        if (!ModelState.IsValid)
        {
            ClientDetailsModel? client =
                await _clientDetailsService.GetClientDetailsAsync(ClientId.Create(Id), cancellationToken);
            if (client is null)
                return NotFound();
            ClientIdDisplay = client.ClientId;
            return Page();
        }

        AdminMutationResult result = await _clientDetailsService.UpdateClientBasicsAsync(ClientId.Create(Id),
            Input.ClientName, Input.Description, cancellationToken);
        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        if (!result.Succeeded)
        {
            MapErrors(result);
            ClientIdDisplay = Id;
            return Page();
        }

        return RedirectToPage("./Details", new { id = Id });
    }

    private void MapErrors(AdminMutationResult result)
    {
        foreach ((string key, string[] messages) in result.Errors)
            foreach (string message in messages)
                ModelState.AddModelError(key, message);
    }
}