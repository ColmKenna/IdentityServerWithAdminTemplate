using IdentityServerProject.Configuration;
using IdentityServerProject.Services;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace IdentityServerProject.Pages.Admin.Users;

public class IndexModel : PageModel
{
    private readonly IUserListService _userListService;
    private readonly AdminConsoleOptions _options;

    public IndexModel(IUserListService userListService, IOptions<AdminConsoleOptions> options)
    {
        _userListService = userListService;
        _options = options.Value;
    }

    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public ListResult<UserListItem> Users { get; private set; } = default!;

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var pagination = Pagination.From(PageNumber, _options.DefaultPageSize);
        PageNumber = pagination.PageNumber;

        Users = await _userListService.GetUsersAsync(new ListQuery(Filter, pagination), cancellationToken);
    }

    public async Task<IActionResult> OnPostUnlockAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var result = await _userListService.UnlockUserAsync(UserId.Create(id), cancellationToken);
        if (result.Status == UserUnlockStatus.NotFound)
            return NotFound();

        if (result.Status == UserUnlockStatus.Failed)
        {
            StatusMessage = result.Errors.Count == 0
                ? "Unable to unlock the user account."
                : $"Unable to unlock the user account: {string.Join(" ", result.Errors)}";
            return RedirectToPage(new { Filter, PageNumber });
        }

        StatusMessage = "User account has been unlocked successfully.";
        return RedirectToPage(new { Filter, PageNumber });
    }
}
