using Duende.IdentityServer.Events;
using Duende.IdentityServer.Extensions;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Account;

public class LogoutModel : PageModel
{
    private readonly SignInManager<Data.ApplicationUser> _signInManager;
    private readonly IIdentityServerInteractionService _interaction;
    private readonly IEventService _events;

    public LogoutModel(
        SignInManager<Data.ApplicationUser> signInManager,
        IIdentityServerInteractionService interaction,
        IEventService events)
    {
        _signInManager = signInManager;
        _interaction = interaction;
        _events = events;
    }

    [BindProperty]
    public string? LogoutId { get; set; }

    public string? PostLogoutRedirectUri { get; set; }
    
    public bool ShowLogoutPrompt { get; set; } = true;

    public async Task<IActionResult> OnGetAsync(string? logoutId)
    {
        LogoutId = logoutId;

        if (logoutId is not null)
        {
            var logoutContext = await _interaction.GetLogoutContextAsync(logoutId, HttpContext.RequestAborted);
            if (logoutContext?.ShowSignoutPrompt == false)
            {
                return await OnPostAsync();
            }
        }

        if (User?.Identity?.IsAuthenticated == true)
        {
            ShowLogoutPrompt = true;
            return Page();
        }
        
        ShowLogoutPrompt = false;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            await _events.RaiseAsync(
                new UserLogoutSuccessEvent(User.GetSubjectId(), User.GetDisplayName()),
                HttpContext.RequestAborted);
            await _signInManager.SignOutAsync();
        }

        if (LogoutId is not null)
        {
            var logoutContext = await _interaction.GetLogoutContextAsync(LogoutId, HttpContext.RequestAborted);
            PostLogoutRedirectUri = logoutContext?.PostLogoutRedirectUri;
        }

        if (PostLogoutRedirectUri is not null)
        {
            return Redirect(PostLogoutRedirectUri);
        }

        return RedirectToPage("/Account/Logout");
    }
}
