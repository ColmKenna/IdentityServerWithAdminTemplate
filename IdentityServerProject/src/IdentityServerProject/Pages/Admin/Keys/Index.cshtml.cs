using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.Keys;

public class IndexModel(IKeyMaterialService keyMaterialService) : PageModel
{
    private readonly IKeyMaterialService _keyMaterialService = keyMaterialService;

    public List<SecurityKeyInfo> Keys { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<SecurityKeyInfo> keys = await _keyMaterialService.GetValidationKeysAsync(cancellationToken);
        Keys = keys.ToList();
    }
}