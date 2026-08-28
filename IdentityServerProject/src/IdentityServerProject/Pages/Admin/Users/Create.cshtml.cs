using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.Users;

public class CreateModel(IUserCreateService userCreateService) : PageModel
{
    private readonly IUserCreateService _userCreateService = userCreateService;

    [BindProperty] public UserCreateInputModel Input { get; set; } = new();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        UserCreateResult result = await _userCreateService.CreateUserAsync(Input, cancellationToken);
        if (!result.Success)
        {
            foreach (string error in result.Errors) ModelState.AddModelError(string.Empty, error);

            return Page();
        }

        return RedirectToPage("./Index");
    }
}