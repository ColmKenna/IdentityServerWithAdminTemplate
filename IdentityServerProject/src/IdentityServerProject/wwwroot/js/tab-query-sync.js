function wireQueryStringTabs(tabsElement, defaultTab) {
    if (!tabsElement || !window.customElements) {
        return;
    }

    const activateTabFromQuery = () => {
        const requestedTab = new URL(window.location.href).searchParams.get('tab') || defaultTab;
        const tabs = Array.from(tabsElement.querySelectorAll('ck-tab'));
        const tabIndex = tabs.findIndex(tab => tab.dataset.tab === requestedTab);
        tabsElement.activateTab(tabIndex >= 0 ? tabIndex : 0);
    };

    customElements.whenDefined('ck-tabs').then(() => {
        tabsElement.addEventListener('tab-selected', event => {
            const selectedTabName = event.detail.selectedTab?.dataset.tab;
            if (!selectedTabName) {
                return;
            }

            const url = new URL(window.location.href);
            if (url.searchParams.get('tab') === selectedTabName) {
                return;
            }

            url.searchParams.set('tab', selectedTabName);
            window.history.pushState(null, '', url);
        });

        window.addEventListener('popstate', activateTabFromQuery);
    });
}
