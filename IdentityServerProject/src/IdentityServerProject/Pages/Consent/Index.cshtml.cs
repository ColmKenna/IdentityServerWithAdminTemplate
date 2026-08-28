using Duende.IdentityServer.Events;
using Duende.IdentityServer.Extensions;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Pages.Consent;

public class IndexModel(
    IIdentityServerInteractionService interaction,
    IEventService events) : PageModel
{
    private readonly IEventService _events = events;
    private readonly IIdentityServerInteractionService _interaction = interaction;

    public string? ReturnUrl { get; set; }
    public string? ClientName { get; set; }
    public IEnumerable<ScopeViewModel> ScopesRequested { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(string returnUrl)
    {
        AuthorizationRequest? request =
            await _interaction.GetAuthorizationContextAsync(returnUrl, HttpContext.RequestAborted);
        if (request is null) return RedirectToPage("/Error");

        ReturnUrl = returnUrl;
        ClientName = request.Client.ClientName ?? request.Client.ClientId;
        ScopesRequested = request.ValidatedResources.Resources.ApiScopes
            .Select(s => new ScopeViewModel(s.Name, s.DisplayName))
            .Concat(request.ValidatedResources.Resources.IdentityResources
                .Select(s => new ScopeViewModel(s.Name, s.DisplayName)));

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string returnUrl, bool consented)
    {
        AuthorizationRequest? request =
            await _interaction.GetAuthorizationContextAsync(returnUrl, HttpContext.RequestAborted);
        if (request is null) return RedirectToPage("/Error");

        ConsentResponse grantedConsent = consented
            ? new ConsentResponse
            {
                RememberConsent = true,
                ScopesValuesConsented = request.ValidatedResources.ParsedScopes.Select(s => s.RawValue).ToArray()
            }
            : new ConsentResponse { Error = InteractionError.AccessDenied };

        await _interaction.GrantConsentAsync(request, grantedConsent, HttpContext.RequestAborted);

        string[] requestedScopes = request.ValidatedResources.ParsedScopes.Select(scope => scope.RawValue).ToArray();
        if (consented)
            await _events.RaiseAsync(
                new ConsentGrantedEvent(
                    User.GetSubjectId(),
                    request.Client.ClientId,
                    requestedScopes,
                    grantedConsent.ScopesValuesConsented ?? [],
                    grantedConsent.RememberConsent),
                HttpContext.RequestAborted);
        else
            await _events.RaiseAsync(
                new ConsentDeniedEvent(User.GetSubjectId(), request.Client.ClientId, requestedScopes),
                HttpContext.RequestAborted);

        return Redirect(returnUrl);
    }

    public record ScopeViewModel(string Value, string? DisplayName);
}