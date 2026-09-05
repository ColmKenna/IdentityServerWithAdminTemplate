using IdentityServerProject.Services.Diagnostics;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin;

/// <summary>
///     One dashboard navigation card. Warning state and badge belong to the shape rather
///     than to a second one, even though only Diagnostics uses them today.
/// </summary>
public sealed record DashboardCard(
    string Page,
    string Id,
    string Icon,
    string Title,
    string Description,
    bool IsWarning = false,
    int? Badge = null);

public sealed record DashboardCardGroup(string Label, IReadOnlyList<DashboardCard> Cards);

public class IndexModel(IDiagnosticsService diagnosticsService) : PageModel
{
    private readonly IDiagnosticsService _diagnosticsService = diagnosticsService;

    public bool HasIssues { get; private set; }

    public List<string> Issues { get; private set; } = new();

    /// <summary>
    ///     The dashboard's card groups, ready to iterate. Prepared after the diagnostics
    ///     run so the Diagnostics card carries the current issue state.
    /// </summary>
    public IReadOnlyList<DashboardCardGroup> CardGroups { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        DiagnosticsModel diagnostics = await _diagnosticsService.GetDiagnosticsAsync(cancellationToken);

        var issues = new List<string>();

        foreach (StoreHealthStatus store in diagnostics.StoreHealth.Where(s => !s.IsHealthy))
            issues.Add($"{store.Name} store is degraded: {store.Detail ?? "connection unhealthy"}.");

        if (string.IsNullOrEmpty(diagnostics.SigningKeyId))
            issues.Add("No active signing key is configured.");

        if (diagnostics.ReservedClaimHolders.Count > 0)
        {
            int count = diagnostics.ReservedClaimHolders.Count;
            issues.Add(
                $"{count} user{(count == 1 ? "" : "s")} holding unauthorized reserved claim type{(count == 1 ? "" : "s")}.");
        }

        Issues = issues;
        HasIssues = issues.Count > 0;
        CardGroups = BuildCardGroups();
    }

    private IReadOnlyList<DashboardCardGroup> BuildCardGroups() =>
    [
        new("Configure",
        [
            new("/Admin/Clients/Index", "dashboard-card-clients", "clients", "Clients",
                "OAuth/OIDC client applications, redirect URIs, and credentials."),
            new("/Admin/Apis/Index", "dashboard-card-apis", "apis", "API Resources",
                "Protected API definitions, secrets, and user claims."),
            new("/Admin/ApiScopes/Index", "dashboard-card-apiscopes", "apiscopes", "API Scopes",
                "Granular token permissions, resource associations, and audiences."),
            new("/Admin/IdentityResources/Index", "dashboard-card-identityresources", "identityresources",
                "Identity Resources", "OpenID Connect scopes such as openid, profile, and email."),
            new("/Admin/Keys/Index", "dashboard-card-keys", "keys", "Signing Keys",
                "Active and historical cryptographic keys for token signing.")
        ]),
        new("Administer",
        [
            new("/Admin/Users/Index", "dashboard-card-users", "users", "Users",
                "Manage accounts, roles, claims, and credentials."),
            new("/Admin/Roles/Index", "dashboard-card-roles", "roles", "Roles",
                "Define and manage roles for access control."),
            new("/Admin/Grants/Index", "dashboard-card-grants", "grants", "Persisted Grants",
                "Active refresh tokens, authorization codes, and consent records."),
            new("/Admin/AuditLogs/Index", "dashboard-card-auditlogs", "auditlogs", "Audit Logs",
                "Security event trail, actor logins, and administrative mutations."),
            new("/Admin/Diagnostics/Index", "dashboard-card-diagnostics", "diagnostics", "Diagnostics",
                "Store connectivity, signing credentials, and claim validation.",
                IsWarning: HasIssues,
                Badge: HasIssues ? Issues.Count : null)
        ])
    ];
}