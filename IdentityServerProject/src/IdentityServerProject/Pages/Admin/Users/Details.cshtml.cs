using System.Security.Claims;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.Users;

public class DetailsModel : PageModel
{
    private readonly IUserDetailsService _userDetailsService;

    public DetailsModel(IUserDetailsService userDetailsService)
    {
        _userDetailsService = userDetailsService;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "overview";

    public UserDetailsModel Account { get; private set; } = default!;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [TempData]
    public string? WarningMessage { get; set; }

    [BindProperty]
    public string? DeleteConfirmation { get; set; }

    private string? CurrentUserId => HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? HttpContext.User.FindFirstValue("sub");

    private UserId TargetUserId => UserId.Create(Id);

    private UserId? CurrentUser => CurrentUserId is { } currentUserId
        ? UserId.Create(currentUserId)
        : null;

    private UserActionContext Context => new(TargetUserId, CurrentUser);

    private bool HasUserId => !string.IsNullOrWhiteSpace(Id);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!HasUserId)
            return NotFound();

        var user = await _userDetailsService.GetUserDetailsAsync(Context, cancellationToken);
        if (user == null)
            return NotFound();

        Account = user;
        return Page();
    }

    public async Task<IActionResult> OnPostUnlockAsync(CancellationToken cancellationToken)
    {
        if (!HasUserId)
            return NotFound();

        var result = await _userDetailsService.UnlockUserAsync(TargetUserId, cancellationToken);
        if (result.Status == UserUnlockStatus.NotFound)
            return NotFound();

        if (result.Status == UserUnlockStatus.Failed)
        {
            ErrorMessage = result.Errors.Count == 0
                ? "Unable to unlock the user account."
                : $"Unable to unlock the user account: {string.Join(" ", result.Errors)}";
            return RedirectToUserTab("overview");
        }

        StatusMessage = "User account has been unlocked successfully.";
        return RedirectToUserTab("overview");
    }

    public async Task<IActionResult> OnPostAddRoleAsync(string role, CancellationToken cancellationToken)
    {
        if (!HasUserId)
            return NotFound();

        var result = await _userDetailsService.AddRoleAsync(TargetUserId, role, cancellationToken);
        if (!result.Success)
        {
            if (result.ErrorMessage is "User not found." or "Role not found.")
                return NotFound();

            ErrorMessage = result.ErrorMessage;
        }
        else
        {
            StatusMessage = $"Role '{role}' added.";
        }

        return RedirectToUserTab("roles");
    }

    public async Task<IActionResult> OnPostRemoveRoleAsync(string role, CancellationToken cancellationToken)
    {
        if (!HasUserId)
            return NotFound();

        var result = await _userDetailsService.RemoveRoleAsync(TargetUserId, role, cancellationToken);
        if (!result.Success)
        {
            if (result.ErrorMessage == "User not found.")
                return NotFound();

            ErrorMessage = result.ErrorMessage;
        }
        else
        {
            StatusMessage = $"Role '{role}' removed.";
        }

        return RedirectToUserTab("roles");
    }

    public async Task<IActionResult> OnPostAddClaimAsync(string claimType, string claimValue, CancellationToken cancellationToken)
    {
        if (!HasUserId)
            return NotFound();

        var result = await _userDetailsService.AddClaimAsync(TargetUserId, new UserClaim(claimType, claimValue), cancellationToken);
        if (!result.Success)
        {
            if (result.ErrorMessage == "User not found.")
                return NotFound();

            ErrorMessage = result.ErrorMessage;
        }
        else
        {
            StatusMessage = "Claim added.";
        }

        return RedirectToUserTab("claims");
    }

    public async Task<IActionResult> OnPostRemoveClaimAsync(string claimType, string claimValue, CancellationToken cancellationToken)
    {
        if (!HasUserId)
            return NotFound();

        var result = await _userDetailsService.RemoveClaimAsync(TargetUserId, new UserClaim(claimType, claimValue), cancellationToken);
        if (!result.Success)
        {
            if (result.ErrorMessage == "User not found.")
                return NotFound();

            ErrorMessage = result.ErrorMessage;
        }
        else
        {
            StatusMessage = "Claim removed.";
        }

        return RedirectToUserTab("claims");
    }

    public async Task<IActionResult> OnPostRevokeUserAccessAsync(CancellationToken cancellationToken)
    {
        if (!HasUserId)
            return NotFound();

        var result = await _userDetailsService.RevokeUserAccessAsync(Context, cancellationToken);
        if (!result.Success)
        {
            if (result.ErrorMessage == "User not found.")
                return NotFound();

            ErrorMessage = result.ErrorMessage;
        }
        else
        {
            StatusMessage = result.RevokedGrantCount == 0
                ? "Access was revoked. No persisted grants were present."
                : $"Access was revoked and {result.RevokedGrantCount} persisted grant(s) were removed.";
            WarningMessage = result.WarningMessage;
        }

        return RedirectToUserTab("access");
    }

    public async Task<IActionResult> OnPostSuspendAsync(CancellationToken cancellationToken)
    {
        if (!HasUserId)
            return NotFound();

        var result = await _userDetailsService.SuspendUserAsync(Context, cancellationToken);
        if (!result.Success)
        {
            if (result.ErrorMessage == "User not found.")
                return NotFound();
            ErrorMessage = result.ErrorMessage;
        }
        else
        {
            StatusMessage = "User account has been suspended indefinitely.";
        }

        return RedirectToUserTab("danger");
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        if (!HasUserId)
            return NotFound();

        if (!string.Equals(DeleteConfirmation?.Trim(), "DELETE", System.StringComparison.Ordinal))
        {
            ErrorMessage = "Type DELETE exactly to confirm permanent deletion.";
            return RedirectToUserTab("danger");
        }

        var result = await _userDetailsService.DeleteUserAsync(Context, cancellationToken);
        if (!result.Success)
        {
            if (result.ErrorMessage == "User not found.")
                return NotFound();
            ErrorMessage = result.ErrorMessage;
            return RedirectToUserTab("danger");
        }

        StatusMessage = "User account was successfully deleted.";
        return RedirectToPage("./Index");
    }

    private RedirectToPageResult RedirectToUserTab(string tab) =>
        RedirectToPage(new { id = Id, tab });
}
