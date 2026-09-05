using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;

namespace IdentityServerProject.Pages.Shared;

/// <summary>
///     Renders a body string that is deliberately written as markup by the developer, while
///     keeping any runtime value out of the unencoded path.
/// </summary>
/// <remarks>
///     The body itself is trusted: it is a literal in a .cshtml file and is emitted through
///     Html.Raw so its tags render. Values that arrive at runtime are not trusted, so they are
///     passed separately as composite-format arguments, HTML-encoded here, and substituted in.
///     Interpolating a runtime value straight into the body string would bypass the encoder,
///     which is the mistake this shape exists to make hard.
/// </remarks>
public static class SafeMarkupBody
{
    public static IHtmlContent Render(string markup, IReadOnlyList<string> args, HtmlEncoder encoder)
    {
        if (args.Count == 0)
        {
            // No runtime values, so nothing to encode and no need to run composite formatting —
            // which also means a literal body may contain braces without being misread.
            return new HtmlString(markup);
        }

        object[] encoded = args.Select(arg => (object)encoder.Encode(arg ?? string.Empty)).ToArray();
        return new HtmlString(string.Format(CultureInfo.InvariantCulture, markup, encoded));
    }
}

public class AuthCardHeaderModel
{
    public string BrandSubtitle { get; }
    public string Title { get; }
    public string? Description { get; }

    public AuthCardHeaderModel(string brandSubtitle, string title, string? description = null)
    {
        BrandSubtitle = brandSubtitle;
        Title = title;
        Description = description;
    }
}

public class SecretRevealBannerModel
{
    public string? SecretValue { get; }
    public string? ClientId { get; }

    /// <summary>Literal markup. Use <c>{0}</c> placeholders for runtime values; see <see cref="DescriptionArgs" />.</summary>
    public string? Description { get; }

    /// <summary>Runtime values substituted into <see cref="Description" />, HTML-encoded on render.</summary>
    public IReadOnlyList<string> DescriptionArgs { get; }

    public SecretRevealBannerModel(
        string? secretValue,
        string? clientId = null,
        string? description = null,
        IReadOnlyList<string>? descriptionArgs = null)
    {
        SecretValue = secretValue;
        ClientId = clientId;
        Description = description;
        DescriptionArgs = descriptionArgs ?? Array.Empty<string>();
    }
}

public class UriInputSectionModel
{
    public string Label { get; }
    public string InputName { get; }
    public string ContainerId { get; }
    public string Placeholder { get; }
    public string AriaLabel { get; }
    public string ButtonText { get; }
    public ICollection<string> Values { get; }

    public UriInputSectionModel(
        string label,
        string inputName,
        string containerId,
        string placeholder,
        string ariaLabel,
        string buttonText,
        ICollection<string> values)
    {
        Label = label;
        InputName = inputName;
        ContainerId = containerId;
        Placeholder = placeholder;
        AriaLabel = ariaLabel;
        ButtonText = buttonText;
        Values = values;
    }
}

/// <summary>
///     One hidden field carried by a confirmation dialog's form. The id is optional and only
///     needed when script fills the value before the dialog opens.
/// </summary>
public sealed record ModalHiddenField(string Name, string? Id = null, string? Value = null);

public class ConfirmationModalModel
{
    public string DialogId { get; }
    public string Title { get; }
    public string Handler { get; }
    public string? ConfirmWord { get; }
    public string SubmitText { get; }
    public string SubmitClass { get; }
    public string? SubmitId { get; }
    public string? RouteName { get; }
    /// <summary>
    ///     Every hidden field the form posts, in render order: the single-field parameters
    ///     first, then any extras. List pages use the extras to carry pagination and filter
    ///     state through the round trip.
    /// </summary>
    public IReadOnlyList<ModalHiddenField> HiddenFields { get; }
    /// <summary>Literal markup. Use <c>{0}</c> placeholders for runtime values; see <see cref="BodyArgs" />.</summary>
    public string BodyHtml { get; }

    /// <summary>Runtime values substituted into <see cref="BodyHtml" />, HTML-encoded on render.</summary>
    public IReadOnlyList<string> BodyArgs { get; }
    public string? BodyId { get; }

    /// <summary>Optional <c>data-state</c> on the body paragraph.</summary>
    public string? BodyState { get; }

    /// <summary>
    ///     Literal markup for a second, initially hidden paragraph explaining why the action
    ///     is blocked. Script swaps which of the two paragraphs is visible.
    /// </summary>
    public string? BlockedBodyHtml { get; }

    public string? BlockedBodyId { get; }

    public ConfirmationModalModel(
        string dialogId,
        string title,
        string handler,
        string bodyHtml,
        string? confirmWord = null,
        string submitText = "Confirm",
        string submitClass = "btn-danger",
        string? submitId = null,
        string? routeName = null,
        string? hiddenInputName = null,
        string? hiddenInputId = null,
        string? hiddenInputValue = null,
        string? bodyId = null,
        IReadOnlyList<string>? bodyArgs = null,
        IReadOnlyList<ModalHiddenField>? hiddenFields = null,
        string? bodyState = null,
        string? blockedBodyHtml = null,
        string? blockedBodyId = null)
    {
        DialogId = dialogId;
        Title = title;
        Handler = handler;
        BodyHtml = bodyHtml;
        ConfirmWord = confirmWord;
        SubmitText = submitText;
        SubmitClass = submitClass;
        SubmitId = submitId;
        RouteName = routeName;
        BodyId = bodyId;
        BodyArgs = bodyArgs ?? Array.Empty<string>();
        BodyState = bodyState;
        BlockedBodyHtml = blockedBodyHtml;
        BlockedBodyId = blockedBodyId;

        List<ModalHiddenField> fields = [];
        if (!string.IsNullOrEmpty(hiddenInputName))
            fields.Add(new ModalHiddenField(hiddenInputName, hiddenInputId, hiddenInputValue));
        if (hiddenFields is not null)
            fields.AddRange(hiddenFields);
        HiddenFields = fields;
    }
}

public class BackLinkModel
{
    public string Page { get; }
    public string Text { get; }
    public IDictionary<string, string>? RouteValues { get; }

    public BackLinkModel(string page = "./Index", string text = "Back to list", IDictionary<string, string>? routeValues = null)
    {
        Page = page;
        Text = text;
        RouteValues = routeValues;
    }
}

public class ScopeChipItem
{
    public string Text { get; }
    public bool IsLocked { get; }
    public string? LockReason { get; }

    public ScopeChipItem(string text, bool isLocked = false, string? lockReason = null)
    {
        Text = text;
        IsLocked = isLocked;
        LockReason = lockReason;
    }
}

public class ScopeChipsRowModel
{
    public IReadOnlyList<ScopeChipItem> Items { get; }
    public string EmptyStateText { get; }
    public string HandlerName { get; }
    public string RouteNameValue { get; }
    public string RouteParamKey { get; }
    public string RouteNameKey { get; }

    public ScopeChipsRowModel(
        IReadOnlyList<ScopeChipItem> items,
        string emptyStateText,
        string handlerName,
        string routeNameValue,
        string routeParamKey = "claimType",
        string routeNameKey = "name")
    {
        Items = items;
        EmptyStateText = emptyStateText;
        HandlerName = handlerName;
        RouteNameValue = routeNameValue;
        RouteParamKey = routeParamKey;
        RouteNameKey = routeNameKey;
    }

    public static ScopeChipsRowModel ForSimpleList(
        IEnumerable<string> items,
        string emptyStateText,
        string handlerName,
        string routeNameValue,
        string routeParamKey = "claimType",
        string routeNameKey = "name")
    {
        return new ScopeChipsRowModel(
            items.Select(i => new ScopeChipItem(i)).ToList(),
            emptyStateText,
            handlerName,
            routeNameValue,
            routeParamKey,
            routeNameKey
        );
    }
}


/// <summary>
///     A grid of scope checkboxes bound to one collection property.
/// </summary>
/// <remarks>
///     Covers the plain case only. The identity-resource grid on the Client Permissions page
///     looks similar but is not the same component: "openid" there renders a disabled checkbox
///     plus a hidden input and a required marker, because it cannot be deselected. Folding that
///     branch in would make this partial answer a question it should not know about.
/// </remarks>
public class ScopeCheckboxGridModel
{
    public IEnumerable<string> Scopes { get; }
    public ICollection<string> Selected { get; }

    /// <summary>
    ///     Names the checkbox group for assistive technology. Every call site already shows a
    ///     heading or tab label, so the legend is rendered visually hidden rather than repeated.
    /// </summary>
    public string Legend { get; }

    public string InputName { get; }

    public ScopeCheckboxGridModel(
        IEnumerable<string> scopes,
        ICollection<string> selected,
        string legend,
        string inputName = "Input.AllowedScopes")
    {
        Scopes = scopes;
        Selected = selected;
        Legend = legend;
        InputName = inputName;
    }
}

/// <summary>
///     The single-input filter form on a list page. Clear renders only while a filter is
///     active, matching the two multi-field filters that already behaved this way.
/// </summary>
public class ListFilterModel
{
    public string InputId { get; }
    public string LabelText { get; }
    public string Placeholder { get; }
    public string? Value { get; }
    public string InputName { get; }

    public ListFilterModel(
        string inputId,
        string labelText,
        string placeholder,
        string? value,
        string inputName = "Filter")
    {
        InputId = inputId;
        LabelText = labelText;
        Placeholder = placeholder;
        Value = value;
        InputName = inputName;
    }
}

/// <summary>
///     One crumb in the header trail. Carries a Razor Page name rather than a URL so the
///     layout can generate the href through routing.
/// </summary>
/// <remarks>
///     Hand-written paths hid a real defect: Users/Details is declared <c>@page</c> with no
///     route template, so it lives at <c>?id=…</c>, but a breadcrumb wrote it as
///     <c>/Admin/Users/Details/{id}</c> — a path that matched no route. Routing knows each
///     page's real shape; a literal only knows what someone typed.
///     <para>A crumb with no <see cref="Page" /> renders as the current, unlinked item.</para>
/// </remarks>
public sealed record Breadcrumb(
    string Text,
    string? Page = null,
    IDictionary<string, string>? RouteValues = null);

/// <summary>
///     One breadcrumb with its position in the trail already decided, so the layout only
///     has to choose markup.
/// </summary>
public sealed record BreadcrumbTrailItem(
    string Text,
    string? Page,
    IDictionary<string, string>? RouteValues,
    bool IsLink,
    bool NeedsSeparator);

public static class BreadcrumbTrail
{
    /// <summary>
    ///     Resolves a trail for rendering. Every crumb but the first is preceded by a
    ///     separator, and the last crumb is the page being viewed, so it stays plain text
    ///     even when it names a page.
    /// </summary>
    public static IReadOnlyList<BreadcrumbTrailItem> Resolve(IEnumerable<Breadcrumb> crumbs)
    {
        List<Breadcrumb> trail = crumbs.ToList();

        return trail
            .Select((crumb, index) => new BreadcrumbTrailItem(
                crumb.Text,
                crumb.Page,
                crumb.RouteValues,
                IsLink: crumb.Page is not null && index < trail.Count - 1,
                NeedsSeparator: index > 0))
            .ToList();
    }
}

/// <summary>
///     Which sidebar section the current request belongs to. Matched on the Razor Pages
///     route rather than the title, because titles vary per sub-page, and on segment
///     boundaries, so "/Admin/Apis" does not also light up on "/Admin/ApiScopes".
/// </summary>
public sealed class AdminNavigationState(string? currentPage)
{
    private readonly string _currentPage = currentPage ?? string.Empty;

    public bool IsActiveSection(string sectionPrefix) =>
        _currentPage.Length > 0 &&
        (_currentPage.Equals(sectionPrefix, StringComparison.OrdinalIgnoreCase) ||
         _currentPage.StartsWith(sectionPrefix + "/", StringComparison.OrdinalIgnoreCase));

    public string ItemClass(string sectionPrefix) =>
        IsActiveSection(sectionPrefix) ? "nav-item active" : "nav-item";
}
