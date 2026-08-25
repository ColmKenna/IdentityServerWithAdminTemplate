using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Services.ApiScopes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.ApiScopes;

public class CreateInputModel
{
    [Required(ErrorMessage = "Scope name is required")]
    [StringLength(200, ErrorMessage = "Scope name must not exceed 200 characters")]
    [Display(Name = "Scope Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Display name must not exceed 200 characters")]
    [Display(Name = "Display Name")]
    public string? DisplayName { get; set; }

    [StringLength(1000, ErrorMessage = "Description must not exceed 1000 characters")]
    public string? Description { get; set; }
}

public class CreateModel : PageModel
{
    private readonly IApiScopeEditorService _apiScopeEditorService;

    public CreateModel(IApiScopeEditorService apiScopeEditorService)
    {
        _apiScopeEditorService = apiScopeEditorService;
    }

    [BindProperty]
    public CreateInputModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            ModelState.AddModelError(nameof(Input.Name), "Scope name is required");
            return Page();
        }

        var result = await _apiScopeEditorService.CreateAsync(
            new CreateApiScopeCommand(
                IdentityServerProject.Services.Scopes.ScopeName.Create(Input.Name),
                Input.DisplayName,
                Input.Description),
            cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(nameof(Input.Name), result.ErrorMessage ?? "Failed to create scope.");
            return Page();
        }

        return RedirectToPage("./Edit", new { name = Input.Name });
    }
}
