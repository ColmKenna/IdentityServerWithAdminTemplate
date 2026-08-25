using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.Clients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Pages.Admin.Clients;

public class AuthenticationInputModel
{
    public bool RequirePkce { get; set; }
    public bool RequireClientSecret { get; set; }

    [Display(Name = "Grant Types")]
    public List<string> GrantTypes { get; set; } = new();

    [Display(Name = "Redirect URIs")]
    public List<string> RedirectUris { get; set; } = new();

    [Display(Name = "Post-Logout Redirect URIs")]
    public List<string> PostLogoutRedirectUris { get; set; } = new();

    [Display(Name = "Allowed CORS Origins")]
    public List<string> CorsOrigins { get; set; } = new();

    [Display(Name = "Front-channel Logout URI")]
    public string? FrontChannelLogoutUri { get; set; }

    [Display(Name = "Front-channel Logout Session Required")]
    public bool FrontChannelLogoutSessionRequired { get; set; }

    [Display(Name = "Back-channel Logout URI")]
    public string? BackChannelLogoutUri { get; set; }

    [Display(Name = "Back-channel Logout Session Required")]
    public bool BackChannelLogoutSessionRequired { get; set; }
}

public class AuthenticationModel : PageModel
{
    private readonly IClientDetailsService _clientDetailsService;

    public AuthenticationModel(IClientDetailsService clientDetailsService)
    {
        _clientDetailsService = clientDetailsService;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    [BindProperty]
    public AuthenticationInputModel Input { get; set; } = new();

    public string ClientNameDisplay { get; private set; } = string.Empty;
    public bool HasDrifted { get; private set; }
    public string? DriftDetails { get; private set; }
    public int ActiveTabIndex { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return NotFound();
        }

        var authentication = await _clientDetailsService.GetClientAuthenticationAsync(ClientId.Create(Id), cancellationToken);
        if (authentication == null)
        {
            return NotFound();
        }

        LoadFromModel(authentication);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return NotFound();
        }

        NormalizeCollectionInputs();
        ValidateInput();

        if (!ModelState.IsValid)
        {
            ActiveTabIndex = ModelState.Keys.Any(key =>
                key.StartsWith("Input.RedirectUris", StringComparison.Ordinal) ||
                key.StartsWith("Input.PostLogoutRedirectUris", StringComparison.Ordinal) ||
                key.StartsWith("Input.FrontChannelLogoutUri", StringComparison.Ordinal) ||
                key.StartsWith("Input.BackChannelLogoutUri", StringComparison.Ordinal) ||
                key.StartsWith("Input.CorsOrigins", StringComparison.Ordinal)) ? 1 : 0;

            return await ReloadPageAsync(cancellationToken);
        }

        var input = new ClientAuthenticationInputModel
        {
            RequirePkce = Input.RequirePkce,
            RequireClientSecret = Input.RequireClientSecret,
            GrantTypes = Input.GrantTypes,
            RedirectUris = Input.RedirectUris,
            PostLogoutRedirectUris = Input.PostLogoutRedirectUris,
            CorsOrigins = Input.CorsOrigins ?? new List<string>(),
            FrontChannelLogoutUri = Input.FrontChannelLogoutUri,
            FrontChannelLogoutSessionRequired = Input.FrontChannelLogoutSessionRequired,
            BackChannelLogoutUri = Input.BackChannelLogoutUri,
            BackChannelLogoutSessionRequired = Input.BackChannelLogoutSessionRequired
        };

        var result = await _clientDetailsService.UpdateClientAuthenticationAsync(ClientId.Create(Id), input, cancellationToken);
        if (result.Status == AdminMutationStatus.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            AddErrorsToModelState(result.Errors);
            return await ReloadPageAsync(cancellationToken);
        }

        return RedirectToPage("./Details", new { id = Id });
    }

    private void LoadFromModel(ClientAuthenticationModel authentication)
    {
        ClientNameDisplay = authentication.ClientName;
        HasDrifted = authentication.HasDrifted;
        DriftDetails = authentication.DriftDetails;

        Input.RequirePkce = authentication.RequirePkce;
        Input.RequireClientSecret = authentication.RequireClientSecret;
        Input.GrantTypes = authentication.GrantTypes;
        Input.RedirectUris = authentication.RedirectUris;
        Input.PostLogoutRedirectUris = authentication.PostLogoutRedirectUris;
        Input.CorsOrigins = authentication.AllowedCorsOrigins;
        Input.FrontChannelLogoutUri = authentication.FrontChannelLogoutUri;
        Input.FrontChannelLogoutSessionRequired = authentication.FrontChannelLogoutSessionRequired;
        Input.BackChannelLogoutUri = authentication.BackChannelLogoutUri;
        Input.BackChannelLogoutSessionRequired = authentication.BackChannelLogoutSessionRequired;
    }

    private void NormalizeCollectionInputs()
    {
        Input.GrantTypes ??= new List<string>();
        Input.RedirectUris ??= new List<string>();
        Input.PostLogoutRedirectUris ??= new List<string>();
        Input.CorsOrigins ??= new List<string>();
    }

    private void ValidateInput()
    {
        if (Input.GrantTypes.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "At least one grant type must be selected.");
        }
        else if (Input.GrantTypes.Any(grantType =>
                     !string.IsNullOrWhiteSpace(grantType) &&
                     grantType.Trim().Length > ValidationConstants.MaxGrantTypeLength))
        {
            ModelState.AddModelError(
                "Input.GrantTypes",
                $"Grant types cannot exceed {ValidationConstants.MaxGrantTypeLength} characters.");
        }

        if (UriValidationHelper.GetInvalidHttpUris(Input.RedirectUris, ValidationConstants.MaxClientRedirectUriLength).Any())
        {
            ModelState.AddModelError("Input.RedirectUris", "Each Redirect URI must be an absolute HTTP or HTTPS URL.");
        }

        if (UriValidationHelper.GetInvalidHttpUris(Input.PostLogoutRedirectUris, ValidationConstants.MaxClientPostLogoutRedirectUriLength).Any())
        {
            ModelState.AddModelError("Input.PostLogoutRedirectUris", "Each Post-Logout Redirect URI must be an absolute HTTP or HTTPS URL.");
        }

        if (Input.CorsOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Any(origin => !UriValidationHelper.TryNormalizeCorsOrigin(origin, ValidationConstants.MaxClientCorsOriginLength, out _)))
        {
            ModelState.AddModelError("Input.CorsOrigins", "Each CORS Origin must be an absolute HTTP or HTTPS URL containing only the scheme and authority (no path, query, or fragment).");
        }

        if (!string.IsNullOrWhiteSpace(Input.FrontChannelLogoutUri) &&
            !UriValidationHelper.IsValidHttpOrHttpsUri(Input.FrontChannelLogoutUri, ValidationConstants.MaxLogoutUriLength))
        {
            ModelState.AddModelError("Input.FrontChannelLogoutUri", "Front-channel logout URI must be an absolute HTTP or HTTPS URL.");
        }

        if (!string.IsNullOrWhiteSpace(Input.BackChannelLogoutUri) &&
            !UriValidationHelper.IsValidHttpOrHttpsUri(Input.BackChannelLogoutUri, ValidationConstants.MaxLogoutUriLength))
        {
            ModelState.AddModelError("Input.BackChannelLogoutUri", "Back-channel logout URI must be an absolute HTTP or HTTPS URL.");
        }
    }

    private async Task<IActionResult> ReloadPageAsync(CancellationToken cancellationToken)
    {
        var authentication = await _clientDetailsService.GetClientAuthenticationAsync(ClientId.Create(Id), cancellationToken);
        if (authentication == null)
        {
            return NotFound();
        }

        ClientNameDisplay = authentication.ClientName;
        HasDrifted = authentication.HasDrifted;
        DriftDetails = authentication.DriftDetails;
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
