using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Configuration;
using IdentityServerProject.Services;
using IdentityServerProject.Services.Roles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace IdentityServerProject.Pages.Admin.Roles;

public class IndexModel : PageModel
{
    private readonly IRoleService _roleService;
    private readonly AdminConsoleOptions _options;

    public IndexModel(IRoleService roleService, IOptions<AdminConsoleOptions> options)
    {
        _roleService = roleService;
        _options = options.Value;
    }

    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public ListResult<RoleListItem> Roles { get; private set; } = default!;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Roles = await _roleService.GetRolesAsync(Filter, PageNumber, _options.DefaultPageSize, cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id, CancellationToken cancellationToken)
    {
        var result = await _roleService.DeleteRoleAsync(id, cancellationToken);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
            return RedirectToPage(new { Filter, PageNumber });
        }

        StatusMessage = "Role successfully deleted.";
        return RedirectToPage(new { Filter, PageNumber });
    }
}
