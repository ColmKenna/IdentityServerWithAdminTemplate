using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Configuration;
using IdentityServerProject.Services;
using IdentityServerProject.Services.Grants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace IdentityServerProject.Pages.Admin.Grants;

public class IndexModel : PageModel
{
    private readonly IGrantListService _grantListService;
    private readonly AdminConsoleOptions _options;

    public IndexModel(IGrantListService grantListService, IOptions<AdminConsoleOptions> options)
    {
        _grantListService = grantListService;
        _options = options.Value;
    }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? SubjectId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ClientId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TypeFilter { get; set; }

    public ListResult<GrantListItem> Grants { get; private set; } = default!;

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var pagination = Pagination.From(PageNumber, _options.DefaultPageSize);
        PageNumber = pagination.PageNumber;

        Grants = await _grantListService.GetGrantsAsync(
            SubjectId,
            ClientId,
            TypeFilter,
            pagination,
            cancellationToken);
    }

    public async Task<IActionResult> OnPostRevokeAsync(string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return NotFound();
        }

        var result = await _grantListService.RevokeGrantAsync(key, cancellationToken);

        if (result == RevokeGrantResult.NotFound)
        {
            return NotFound();
        }

        SuccessMessage = "Grant revoked successfully.";
        return RedirectToPage(new { PageNumber, SubjectId, ClientId, TypeFilter });
    }
}
