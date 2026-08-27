namespace IdentityServerProject.Pages.Admin.Clients;

/// <summary>
/// Identifies which of the Client editor sub-pages is currently being rendered, so the shared
/// tab strip (_ClientEditorTabs.cshtml) knows which tab to mark active.
/// </summary>
public enum ClientEditorTab
{
    Basics,
    Authentication,
    Permissions,
    Secrets,
    TokenSettings
}

/// <summary>
/// One entry in the Client editor sub-navigation: the tab it identifies, the label shown on the
/// tab heading, and the Razor Page it links to.
/// </summary>
/// <param name="Tab">The tab this entry represents.</param>
/// <param name="Label">Text rendered on the tab heading.</param>
/// <param name="Page">Absolute Razor Page path, used with an <c>id</c> route value.</param>
public sealed record ClientEditorTabLink(ClientEditorTab Tab, string Label, string Page);

/// <summary>
/// View model for the tab strip shared by the five Client editor pages. Callers pass the client
/// being edited and which tab they are; the tab set itself is fixed and lives here so the five
/// pages cannot drift out of sync with one another.
/// </summary>
public sealed class ClientEditorTabsModel
{
    /// <summary>
    /// The Client editor sub-pages, in the order they appear in the tab strip.
    /// </summary>
    public static IReadOnlyList<ClientEditorTabLink> Tabs { get; } = new[]
    {
        new ClientEditorTabLink(ClientEditorTab.Basics, "Basics", "/Admin/Clients/Basics"),
        new ClientEditorTabLink(ClientEditorTab.Authentication, "Authentication", "/Admin/Clients/Authentication"),
        new ClientEditorTabLink(ClientEditorTab.Permissions, "Permissions", "/Admin/Clients/Permissions"),
        new ClientEditorTabLink(ClientEditorTab.Secrets, "Secrets", "/Admin/Clients/Secrets"),
        new ClientEditorTabLink(ClientEditorTab.TokenSettings, "Token Settings", "/Admin/Clients/TokenSettings")
    };

    public ClientEditorTabsModel(string clientId, ClientEditorTab currentTab)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        ClientId = clientId;
        CurrentTab = currentTab;
    }

    /// <summary>The ClientId used as the <c>id</c> route value on every tab link.</summary>
    public string ClientId { get; }

    /// <summary>The sub-page currently being rendered.</summary>
    public ClientEditorTab CurrentTab { get; }
}
