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
    public string? HiddenInputName { get; }
    public string? HiddenInputId { get; }
    public string? HiddenInputValue { get; }
    /// <summary>Literal markup. Use <c>{0}</c> placeholders for runtime values; see <see cref="BodyArgs" />.</summary>
    public string BodyHtml { get; }

    /// <summary>Runtime values substituted into <see cref="BodyHtml" />, HTML-encoded on render.</summary>
    public IReadOnlyList<string> BodyArgs { get; }
    public string? BodyId { get; }

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
        IReadOnlyList<string>? bodyArgs = null)
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
        HiddenInputName = hiddenInputName;
        HiddenInputId = hiddenInputId;
        HiddenInputValue = hiddenInputValue;
        BodyId = bodyId;
        BodyArgs = bodyArgs ?? Array.Empty<string>();
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

