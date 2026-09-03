using System.ComponentModel.DataAnnotations;
using Duende.IdentityServer.Events;
using Duende.IdentityServer.Services;
using IdentityServerProject.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace IdentityServerProject.Pages.Account;

public class LoginModel(
    SignInManager<ApplicationUser> signInManager,
    IIdentityServerInteractionService interaction,
    IEventService events) : PageModel
{
    private readonly IEventService _events = events;
    private readonly IIdentityServerInteractionService _interaction = interaction;
    private readonly SignInManager<ApplicationUser> _signInManager = signInManager;

    [BindProperty] public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet(string? returnUrl) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid) return Page();

        SignInResult result = await _signInManager.PasswordSignInAsync(
            Input.Username, Input.Password, false, true);

        if (result.Succeeded)
        {
            ApplicationUser? user = await _signInManager.UserManager.FindByNameAsync(Input.Username);
            await _events.RaiseAsync(new UserLoginSuccessEvent(Input.Username, user!.Id, Input.Username),
                HttpContext.RequestAborted);

            if (returnUrl is not null && (_interaction.IsValidReturnUrl(returnUrl) || Url.IsLocalUrl(returnUrl)))
            {
                return Redirect(returnUrl);
            }

            return Redirect("~/");
        }

        await _events.RaiseAsync(new UserLoginFailureEvent(Input.Username, "invalid credentials"),
            HttpContext.RequestAborted);
        ErrorMessage = "Invalid username or password.";
        return Page();
    }

    public class InputModel
    {
        [Required] public string Username { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }
}