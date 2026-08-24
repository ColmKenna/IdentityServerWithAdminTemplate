// Client editor sub-navigation (WI-07).
//
// Pages/Admin/Clients/_ClientEditorTabs.cshtml renders the five Client editor pages as a single
// <ck-tabs> strip. Unlike the in-page tab strips on those same pages, this one is *navigation*:
// each <ck-tab> holds a link to a sibling page instead of panel content. Two adjustments turn the
// component into that:
//
//   1. The component always renders a panel per tab (fixed height, see the vendored
//      lib/ck-tabs-webcomponent/index.esm.js). There is no content to put in them here, so they
//      are hidden. The shadow root is open, so a stylesheet is appended to it -- CSS custom
//      properties cannot reach .panel, which sets height in px rather than through a variable.
//   2. Selecting a tab navigates to that tab's link. The component activates tabs on selection
//      (including via arrow keys), so the strip behaves like tabs with automatic activation.
//
// If the ck-tabs module never loads, whenDefined() never settles, nothing here runs, and the
// strip stays in its un-upgraded state: a plain row of links styled by
// .client-editor-nav:not(:defined) in admin.css.

const NAV_SHADOW_CSS = `
  /* Navigation strip: the tabs are links, so the panels have no content to show. */
  .panel { display: none !important; }

  /* Sub-navigation proportions -- the component's defaults are sized for content tabs. */
  .tab-heading {
    padding: 12px 18px;
    font-size: 0.95rem;
    font-weight: 600;
  }
`;

/**
 * Appends the navigation-strip overrides to a ck-tabs instance's open shadow root.
 * @param {HTMLElement & { shadowRoot: ShadowRoot | null }} nav
 */
function applyNavStyles(nav) {
    const shadow = nav.shadowRoot;
    if (!shadow) {
        return;
    }

    if ('adoptedStyleSheets' in shadow && 'replaceSync' in CSSStyleSheet.prototype) {
        try {
            const sheet = new CSSStyleSheet();
            sheet.replaceSync(NAV_SHADOW_CSS);
            // Appended after the component's own sheet so these rules win on equal specificity.
            shadow.adoptedStyleSheets = [...shadow.adoptedStyleSheets, sheet];
            return;
        } catch {
            // Fall through to the <style> fallback below.
        }
    }

    const style = document.createElement('style');
    style.textContent = NAV_SHADOW_CSS;
    shadow.appendChild(style);
}

/**
 * Navigates to the selected tab's page. The current tab is skipped so re-selecting it does not
 * reload the page the user is already on.
 * @param {CustomEvent} event
 */
function navigateToSelectedTab(event) {
    const tab = event.detail && event.detail.selectedTab;
    if (!tab || tab.dataset.current === 'true') {
        return;
    }

    const link = tab.querySelector('a[href]');
    if (link) {
        window.location.assign(link.href);
    }
}

customElements.whenDefined('ck-tabs').then(() => {
    document.querySelectorAll('ck-tabs[data-client-editor-nav]').forEach((nav) => {
        applyNavStyles(nav);
        nav.addEventListener('tab-selected', navigateToSelectedTab);
    });
});
