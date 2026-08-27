using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.Clients;

public class PermissionsInputModel
{
    public List<string> AllowedScopes { get; set; } = new();
}

public class PermissionsModel : PageModel
{
    private readonly IClientDetailsService _clientDetailsService;

    public PermissionsModel(IClientDetailsService clientDetailsService)
    {
        _clientDetailsService = clientDetailsService;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    [BindProperty]
    public PermissionsInputModel Input { get; set; } = new();

    public string ClientNameDisplay { get; private set; } = string.Empty;
    public bool IsInteractive { get; private set; }
    public List<string> AvailableIdentityScopes { get; private set; } = new();
    public List<string> AvailableApiScopes { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
            return NotFound();

        var permissions = await _clientDetailsService.GetClientPermissionsAsync(ClientId.Create(Id), cancellationToken);
        if (permissions == null)
            return NotFound();

        LoadFromModel(permissions);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
            return NotFound();

        var result = await _clientDetailsService.UpdateClientPermissionsAsync(
            ClientId.Create(Id), ScopeSet.FromStrings(Input.AllowedScopes), cancellationToken);
        if (result.Status == AdminMutationStatus.NotFound)
            return NotFound();

        if (!result.Succeeded)
        {
            foreach (var (key, messages) in result.Errors)
            {
                foreach (var message in messages)
                {
                    ModelState.AddModelError(key, message);
                }
            }

            var permissions = await _clientDetailsService.GetClientPermissionsAsync(ClientId.Create(Id), cancellationToken);
            if (permissions == null)
                return NotFound();

            ClientNameDisplay = permissions.ClientName;
            IsInteractive = permissions.IsInteractive;
            AvailableIdentityScopes = permissions.AvailableIdentityScopes;
            AvailableApiScopes = permissions.AvailableApiScopes;
            return Page();
        }

        return RedirectToPage("./Details", new { id = Id });
    }

    private void LoadFromModel(ClientPermissionsModel permissions)
    {
        ClientNameDisplay = permissions.ClientName;
        IsInteractive = permissions.IsInteractive;
        AvailableIdentityScopes = permissions.AvailableIdentityScopes;
        AvailableApiScopes = permissions.AvailableApiScopes;

        Input.AllowedScopes = permissions.AllowedScopes;
    }
}
