using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Configuration;
using IdentityServerProject.Services;
using IdentityServerProject.Services.Clients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace IdentityServerProject.Pages.Admin.Clients;

public class IndexModel : PageModel
{
    private readonly IClientListService _clientListService;
    private readonly AdminConsoleOptions _options;

    public IndexModel(IClientListService clientListService, IOptions<AdminConsoleOptions> options)
    {
        _clientListService = clientListService;
        _options = options.Value;
    }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    public ListResult<ClientListItem> Clients { get; private set; } = default!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var pagination = Pagination.From(PageNumber, _options.DefaultPageSize);
        PageNumber = pagination.PageNumber;

        Clients = await _clientListService.GetClientsAsync(Filter, pagination, cancellationToken);
    }
}
