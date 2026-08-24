using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.Diagnostics;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.Diagnostics;

public class IndexModel : PageModel
{
    private readonly IDiagnosticsService _diagnosticsService;

    public IndexModel(IDiagnosticsService diagnosticsService)
    {
        _diagnosticsService = diagnosticsService;
    }

    public DiagnosticsModel Diagnostics { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Diagnostics = await _diagnosticsService.GetDiagnosticsAsync(cancellationToken);
    }
}
