namespace IdentityServerProject.Pages.Shared;

public class SecretRevealBannerModel
{
    public string? SecretValue { get; }
    public string? ClientId { get; }
    public string? Description { get; }

    public SecretRevealBannerModel(string? secretValue, string? clientId = null, string? description = null)
    {
        SecretValue = secretValue;
        ClientId = clientId;
        Description = description;
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
    public string BodyHtml { get; }
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
        string? bodyId = null)
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

