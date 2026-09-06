using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Account;

public class AccessDeniedModel : PageModel
{
    private const string ConsoleRoot = "/Admin";

    /// <summary>
    ///     Where both login links send the user: the page they were refused, when the
    ///     query string carried one, and otherwise the console root.
    /// </summary>
    public string LoginReturnUrl { get; private set; } = ConsoleRoot;

    public void OnGet(string? returnUrl) =>
        LoginReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? ConsoleRoot : returnUrl;
}
