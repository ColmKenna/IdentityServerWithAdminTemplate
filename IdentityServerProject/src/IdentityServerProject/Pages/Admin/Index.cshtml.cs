using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.Diagnostics;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin;

public class IndexModel : PageModel
{
    private readonly IDiagnosticsService _diagnosticsService;

    public IndexModel(IDiagnosticsService diagnosticsService)
    {
        _diagnosticsService = diagnosticsService;
    }

    public bool HasIssues { get; private set; }

    public List<string> Issues { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var diagnostics = await _diagnosticsService.GetDiagnosticsAsync(cancellationToken);

        var issues = new List<string>();

        foreach (var store in diagnostics.StoreHealth.Where(s => !s.IsHealthy))
        {
            issues.Add($"{store.Name} store is degraded: {store.Detail ?? "connection unhealthy"}.");
        }

        if (string.IsNullOrEmpty(diagnostics.SigningKeyId))
            issues.Add("No active signing key is configured.");

        if (diagnostics.ReservedClaimHolders.Count > 0)
        {
            var count = diagnostics.ReservedClaimHolders.Count;
            issues.Add($"{count} user{(count == 1 ? "" : "s")} holding unauthorized reserved claim type{(count == 1 ? "" : "s")}.");
        }

        Issues = issues;
        HasIssues = issues.Count > 0;
    }
}
