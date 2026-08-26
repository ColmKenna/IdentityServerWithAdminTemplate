using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.IdentityResources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.IdentityResources;

public class CreateInputModel
{
    [Required(ErrorMessage = "Resource name is required")]
    [StringLength(200, ErrorMessage = "Resource name must not exceed 200 characters")]
    [Display(Name = "Resource Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Display name must not exceed 200 characters")]
    [Display(Name = "Display Name")]
    public string? DisplayName { get; set; }

    [StringLength(1000, ErrorMessage = "Description must not exceed 1000 characters")]
    public string? Description { get; set; }

    public bool Enabled { get; set; } = true;

    public bool Required { get; set; }

    public bool Emphasize { get; set; }

    public bool ShowInDiscoveryDocument { get; set; } = true;

    public List<string> UserClaims { get; set; } = new();
}

public class CreateModel : PageModel
{
    private readonly IIdentityResourceEditorService _identityResourceEditorService;

    public CreateModel(IIdentityResourceEditorService identityResourceEditorService)
    {
        _identityResourceEditorService = identityResourceEditorService;
    }

    [BindProperty]
    public CreateInputModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        var result = await _identityResourceEditorService.CreateAsync(
            new CreateIdentityResourceCommand(
                IdentityServerProject.Services.Scopes.ScopeName.Create(Input.Name),
                Input.DisplayName,
                Input.Description,
                Input.Enabled,
                Input.Required,
                Input.Emphasize,
                Input.ShowInDiscoveryDocument,
                Input.UserClaims),
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(Input.Name), result.ErrorMessage ?? "Failed to create identity resource.");
            return Page();
        }

        return RedirectToPage("./Edit", new { name = Input.Name });
    }
}
