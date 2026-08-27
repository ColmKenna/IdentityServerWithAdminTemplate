using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.Keys;

public class IndexModel : PageModel
{
    private readonly IKeyMaterialService _keyMaterialService;

    public IndexModel(IKeyMaterialService keyMaterialService)
    {
        _keyMaterialService = keyMaterialService;
    }

    public List<SecurityKeyInfo> Keys { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var keys = await _keyMaterialService.GetValidationKeysAsync(cancellationToken);
        Keys = keys.ToList();
    }
}
