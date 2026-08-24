using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Admin.Clients;

public class TokenSettingsInputModel
{
    [Range(ValidationConstants.MinAccessTokenLifetime, ValidationConstants.MaxAccessTokenLifetime, ErrorMessage = "Access Token Lifetime must be between 60 and 86400 seconds (1 minute to 24 hours).")]
    [Display(Name = "Access Token Lifetime (seconds)")]
    public int AccessTokenLifetime { get; set; }

    [Range(ValidationConstants.MinIdentityTokenLifetime, ValidationConstants.MaxIdentityTokenLifetime, ErrorMessage = "Identity Token Lifetime must be between 60 and 3600 seconds (1 minute to 1 hour).")]
    [Display(Name = "Identity Token Lifetime (seconds)")]
    public int IdentityTokenLifetime { get; set; }

    [Display(Name = "Require Consent")]
    public bool RequireConsent { get; set; }

    [Display(Name = "Allow Offline Access")]
    public bool AllowOfflineAccess { get; set; }

    [Display(Name = "Refresh Token Usage")]
    public int RefreshTokenUsage { get; set; }

    [Display(Name = "Refresh Token Expiration")]
    public int RefreshTokenExpiration { get; set; }

    [Display(Name = "Absolute Refresh Token Lifetime (seconds)")]
    public int AbsoluteRefreshTokenLifetime { get; set; }

    [Display(Name = "Sliding Refresh Token Lifetime (seconds)")]
    public int SlidingRefreshTokenLifetime { get; set; }
}

public class TokenSettingsModel : PageModel
{
    private readonly IClientDetailsService _clientDetailsService;

    public TokenSettingsModel(IClientDetailsService clientDetailsService)
    {
        _clientDetailsService = clientDetailsService;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    [BindProperty]
    public TokenSettingsInputModel Input { get; set; } = new();

    public string ClientNameDisplay { get; private set; } = string.Empty;
    public int ActiveTabIndex { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return NotFound();
        }

        var settings = await _clientDetailsService.GetClientTokenSettingsAsync(Id, cancellationToken);
        if (settings == null)
        {
            return NotFound();
        }

        LoadFromModel(settings);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            ActiveTabIndex = ModelState.Keys.Any(key =>
                key.StartsWith("Input.RequireConsent", StringComparison.Ordinal) ||
                key.StartsWith("Input.AllowOfflineAccess", StringComparison.Ordinal)) ? 1 : 0;

            return await ReloadPageAsync(cancellationToken);
        }

        var input = new ClientTokenSettingsInputModel
        {
            AccessTokenLifetime = Input.AccessTokenLifetime,
            IdentityTokenLifetime = Input.IdentityTokenLifetime,
            RequireConsent = Input.RequireConsent,
            AllowOfflineAccess = Input.AllowOfflineAccess,
            RefreshTokenUsage = Input.RefreshTokenUsage,
            RefreshTokenExpiration = Input.RefreshTokenExpiration,
            AbsoluteRefreshTokenLifetime = Input.AbsoluteRefreshTokenLifetime,
            SlidingRefreshTokenLifetime = Input.SlidingRefreshTokenLifetime
        };

        var result = await _clientDetailsService.UpdateClientTokenSettingsAsync(Id, input, cancellationToken);
        if (result.Status == AdminMutationStatus.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            AddErrorsToModelState(result.Errors);
            ActiveTabIndex = result.Errors.Keys.Any(key =>
                key.StartsWith("Input.RequireConsent", StringComparison.Ordinal) ||
                key.StartsWith("Input.AllowOfflineAccess", StringComparison.Ordinal)) ? 1 : 0;
            return await ReloadPageAsync(cancellationToken);
        }

        return RedirectToPage("./Details", new { id = Id });
    }

    private void LoadFromModel(ClientTokenSettingsModel settings)
    {
        ClientNameDisplay = settings.ClientName;

        Input.AccessTokenLifetime = settings.AccessTokenLifetime;
        Input.IdentityTokenLifetime = settings.IdentityTokenLifetime;
        Input.RequireConsent = settings.RequireConsent;
        Input.AllowOfflineAccess = settings.AllowOfflineAccess;
        Input.RefreshTokenUsage = settings.RefreshTokenUsage;
        Input.RefreshTokenExpiration = settings.RefreshTokenExpiration;
        Input.AbsoluteRefreshTokenLifetime = settings.AbsoluteRefreshTokenLifetime;
        Input.SlidingRefreshTokenLifetime = settings.SlidingRefreshTokenLifetime;
    }

    private async Task<IActionResult> ReloadPageAsync(CancellationToken cancellationToken)
    {
        var settings = await _clientDetailsService.GetClientTokenSettingsAsync(Id, cancellationToken);
        if (settings == null)
        {
            return NotFound();
        }

        ClientNameDisplay = settings.ClientName;
        return Page();
    }

    private void AddErrorsToModelState(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (key, messages) in errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(key, message);
            }
        }
    }
}
