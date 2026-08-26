using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.Users;

public class ResetPasswordInputModel
{
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "New Password")]
    public string NewPassword { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("NewPassword", ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ResetPasswordModel : PageModel
{
    private readonly IUserDetailsService _userDetailsService;

    public ResetPasswordModel(IUserDetailsService userDetailsService)
    {
        _userDetailsService = userDetailsService;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    [BindProperty]
    public ResetPasswordInputModel Input { get; set; } = new();

    public string UserNameDisplay { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return NotFound();
        }

        return await LoadPageAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return await LoadPageAsync(cancellationToken);
        }

        var result = await _userDetailsService.ResetPasswordAsync(UserId.Create(Id), Input.NewPassword, cancellationToken);
        
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Password reset failed.");
            return await LoadPageAsync(cancellationToken);
        }

        TempData["StatusMessage"] = "Password has been successfully reset.";
        return RedirectToPage("./Details", new { id = Id });
    }

    private async Task<IActionResult> LoadPageAsync(CancellationToken cancellationToken)
    {
        var account = await _userDetailsService.GetUserDetailsAsync(new UserActionContext(UserId.Create(Id), null), cancellationToken);
        if (account == null)
        {
            return NotFound();
        }

        UserNameDisplay = string.IsNullOrWhiteSpace(account.FullName) ? account.UserName : account.FullName;
        return Page();
    }
}
