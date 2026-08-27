using IdentityServerProject.Configuration;
using IdentityServerProject.Services;
using IdentityServerProject.Services.IdentityResources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace IdentityServerProject.Pages.Admin.IdentityResources;

public class IndexModel : PageModel
{
    private readonly IIdentityResourceListService _identityResourceListService;
    private readonly AdminConsoleOptions _options;

    public IndexModel(IIdentityResourceListService identityResourceListService, IOptions<AdminConsoleOptions> options)
    {
        _identityResourceListService = identityResourceListService;
        _options = options.Value;
    }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    public ListResult<IdentityResourceListItem> IdentityResources { get; private set; } = default!;

    [TempData]
    public string? DeleteErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var pagination = Pagination.From(PageNumber, _options.DefaultPageSize);
        PageNumber = pagination.PageNumber;

        IdentityResources = await _identityResourceListService.GetIdentityResourcesAsync(new ListQuery(Filter, pagination), cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
            return NotFound();

        var result = await _identityResourceListService.DeleteIdentityResourceAsync(name, cancellationToken);

        switch (result)
        {
            case IdentityResourceDeleteResult.NotFound:
                return NotFound();
            case IdentityResourceDeleteResult.Blocked:
                DeleteErrorMessage = $"Identity resource '{name}' is in use or non-editable and cannot be deleted.";
                break;
        }

        return RedirectToPage(new { Filter, PageNumber });
    }
}
