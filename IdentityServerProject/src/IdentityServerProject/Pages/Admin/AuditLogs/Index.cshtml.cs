using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Configuration;
using IdentityServerProject.Services;
using IdentityServerProject.Services.AuditLogs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace IdentityServerProject.Pages.Admin.AuditLogs;

public class IndexModel : PageModel
{
    private readonly IAuditLogListService _auditLogListService;
    private readonly AdminConsoleOptions _options;

    public IndexModel(IAuditLogListService auditLogListService, IOptions<AdminConsoleOptions> options)
    {
        _auditLogListService = auditLogListService;
        _options = options.Value;
    }

    [BindProperty(SupportsGet = true)]
    public AuditLogFilter Filter { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public ListResult<AuditLogListItem> AuditLogEntries { get; private set; } = default!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var pagination = Pagination.From(PageNumber, _options.DefaultPageSize);
        PageNumber = pagination.PageNumber;

        AuditLogEntries = await _auditLogListService.GetAuditLogEntriesAsync(Filter, pagination, cancellationToken);
    }
}
