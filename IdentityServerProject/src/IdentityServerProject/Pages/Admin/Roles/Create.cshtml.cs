using System.ComponentModel.DataAnnotations;
using IdentityServerProject.Services.Roles;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.Roles;

public class CreateModel : PageModel
{
    private readonly IRoleService _roleService;

    public CreateModel(IRoleService roleService)
    {
        _roleService = roleService;
    }

    [BindProperty] public InputModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        RoleCreateResult result = await _roleService.CreateRoleAsync(new RoleCreateInputModel
        {
            Name = Input.Name
        }, cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage!);
            return Page();
        }

        TempData["StatusMessage"] = $"Role '{Input.Name}' was successfully created.";
        return RedirectToPage("./Index");
    }

    public class InputModel
    {
        [Required(ErrorMessage = "Role Name is required")]
        [StringLength(ValidationConstants.MaxNameLength, ErrorMessage = "Role Name must not exceed 200 characters")]
        [Display(Name = "Role Name")]
        public string Name { get; set; } = string.Empty;
    }
}