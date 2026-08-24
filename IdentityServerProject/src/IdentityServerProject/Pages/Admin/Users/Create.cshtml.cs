using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.Users;

public class CreateModel : PageModel
{
    private readonly IUserCreateService _userCreateService;

    public CreateModel(IUserCreateService userCreateService)
    {
        _userCreateService = userCreateService;
    }

    [BindProperty]
    public UserCreateInputModel Input { get; set; } = new();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _userCreateService.CreateUserAsync(Input, cancellationToken);
        if (!result.Success)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        return RedirectToPage("./Index");
    }
}
