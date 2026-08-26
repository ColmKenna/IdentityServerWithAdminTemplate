using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Configuration;
using IdentityServerProject.Services;
using IdentityServerProject.Services.ApiScopes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace IdentityServerProject.Pages.Admin.ApiScopes;

public class IndexModel : PageModel
{
    private readonly IApiScopeListService _apiScopeListService;
    private readonly AdminConsoleOptions _options;

    public IndexModel(IApiScopeListService apiScopeListService, IOptions<AdminConsoleOptions> options)
    {
        _apiScopeListService = apiScopeListService;
        _options = options.Value;
    }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    public ListResult<ApiScopeListItem> ApiScopes { get; private set; } = default!;

    [TempData]
    public string? DeleteErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var pagination = Pagination.From(PageNumber, _options.DefaultPageSize);
        PageNumber = pagination.PageNumber;

        ApiScopes = await _apiScopeListService.GetApiScopesAsync(new ListQuery(Filter, pagination), cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
            return NotFound();

        var result = await _apiScopeListService.DeleteApiScopeAsync(name, cancellationToken);

        switch (result)
        {
            case ApiScopeDeleteResult.NotFound:
                return NotFound();
            case ApiScopeDeleteResult.Blocked:
                DeleteErrorMessage = $"'{name}' is still assigned to one or more clients and cannot be deleted.";
                break;
        }

        return RedirectToPage(new { Filter, PageNumber });
    }
}
