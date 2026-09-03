document.addEventListener('DOMContentLoaded', initializeAdminPage);

function initializeAdminPage() {
    const sidebar = initializeSidebar();

    initializeSidebarGroups(sidebar.setCollapsed);
    initializeUserMenu();
    initializeTooltips(sidebar.burger);
    initializeTableFiltering();
    initializeScopeDeletionDialog();
    initializeDialogCloseButtons();
    initializeCopyButtons();
    initializeEditorTabs();
}

function initializeSidebar() {
    const storageKey = 'admin_sidebar_collapsed';
    document.documentElement.classList.remove('sidebar-collapsed-preload');

    function setCollapsed(collapsed) {
        document.body.classList.toggle('collapsed', collapsed);
        document.documentElement.classList.remove('sidebar-collapsed-preload');

        try {
            localStorage.setItem(storageKey, String(collapsed));
        } catch {
        }
    }

    try {
        if (localStorage.getItem(storageKey) === 'true') {
            document.body.classList.add('collapsed');
        }
    } catch {
    }

    const burger = document.getElementById('burger');
    if (burger) {
        burger.addEventListener('click', () => {
            setCollapsed(!document.body.classList.contains('collapsed'));
        });
    }

    return {burger, setCollapsed};
}

function initializeSidebarGroups(setCollapsed) {
    document.querySelectorAll('.group-toggle').forEach(button => {
        button.addEventListener('click', () => {
            const group = button.parentElement;

            if (document.body.classList.contains('collapsed') && window.innerWidth > 760) {
                setCollapsed(false);
                group.classList.add('open');
                return;
            }

            group.classList.toggle('open');
        });
    });
}

function initializeUserMenu() {
    const userMenu = document.getElementById('userMenu');
    const userButton = document.getElementById('userBtn');

    if (!userMenu || !userButton) {
        return;
    }

    userButton.addEventListener('click', () => {
        const isOpen = userMenu.classList.toggle('open');
        userButton.setAttribute('aria-expanded', String(isOpen));
    });

    document.addEventListener('click', event => {
        if (!userMenu.contains(event.target)) {
            userMenu.classList.remove('open');
            userButton.setAttribute('aria-expanded', 'false');
        }
    });

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') {
            userMenu.classList.remove('open');
            userButton.setAttribute('aria-expanded', 'false');
        }
    });
}

function initializeTooltips(burger) {
    const tooltip = document.createElement('div');
    tooltip.className = 'tip';
    document.body.appendChild(tooltip);

    let tooltipTarget;

    function hideTooltip() {
        tooltip.style.display = 'none';
        tooltipTarget = null;
    }

    function showTooltip(target) {
        if (!document.body.classList.contains('collapsed') || window.innerWidth <= 760 || !target.dataset.tip) {
            return;
        }

        tooltipTarget = target;
        tooltip.textContent = target.dataset.tip;
        tooltip.style.display = 'block';

        const targetBounds = target.getBoundingClientRect();
        tooltip.style.top = `${targetBounds.top + targetBounds.height / 2 - tooltip.offsetHeight / 2}px`;
        tooltip.style.left = `${targetBounds.right + 8}px`;
    }

    document.querySelectorAll('[data-tip]').forEach(element => {
        element.addEventListener('mouseenter', () => showTooltip(element));
        element.addEventListener('mouseleave', hideTooltip);
        element.addEventListener('focus', () => showTooltip(element));
        element.addEventListener('blur', hideTooltip);
    });

    if (burger) {
        burger.addEventListener('click', hideTooltip);
    }

    document.querySelector('.sidebar-scroll')?.addEventListener('scroll', hideTooltip);
}

function initializeTableFiltering() {
    document.querySelectorAll('input[data-table]').forEach(input => {
        const table = document.querySelector(`.${input.dataset.table}`);

        if (!table) {
            return;
        }

        input.addEventListener('input', () => {
            const filterText = input.value.toLowerCase();

            table.querySelectorAll('ck-responsive-row').forEach(row => {
                row.style.display = row.textContent.toLowerCase().includes(filterText) ? '' : 'none';
            });
        });
    });
}

function initializeScopeDeletionDialog() {
    document.querySelectorAll('[data-action="delete-scope"]').forEach(button => {
        button.addEventListener('click', () => {
            const dialog = document.getElementById('delete-scope-modal');
            if (!dialog) {
                return;
            }

            const scopeName = button.dataset.deleteName || '';
            const referenceCount = parseInt(button.dataset.referenceCount || '0', 10);
            const isReferenced = referenceCount > 0;

            dialog.querySelector('#delete-scope-modal-name-input').value = scopeName;
            dialog.querySelector('#delete-scope-modal-name-text').textContent = scopeName;
            dialog.querySelector('#delete-scope-modal-blocked-name-text').textContent = scopeName;
            dialog.querySelector('#delete-scope-modal-blocked-count').textContent = referenceCount;
            dialog.querySelector('#delete-scope-modal-message').hidden = isReferenced;
            dialog.querySelector('#delete-scope-modal-blocked-message').hidden = !isReferenced;
            dialog.querySelector('#delete-scope-modal-confirm').disabled = isReferenced;

            dialog.showModal();
        });
    });
}

function initializeDialogCloseButtons() {
    document.querySelectorAll('[data-action="close-modal"]').forEach(button => {
        button.addEventListener('click', () => button.closest('dialog').close());
    });
}

function initializeCopyButtons() {
    document.querySelectorAll('[data-copy-target]').forEach(button => {
        button.addEventListener('click', () => {
            const target = document.getElementById(button.dataset.copyTarget);
            if (!target) return;
            const textToCopy = target.value !== undefined ? target.value : (target.textContent || '');
            navigator.clipboard.writeText(textToCopy).then(() => {
                const origText = button.textContent;
                button.textContent = 'Copied!';
                setTimeout(() => {
                    button.textContent = origText;
                }, 2000);
            });
        });
    });
}

function copySecretToClipboard() {
    const input = document.getElementById('generated-secret-input');
    if (!input) return;

    input.select();
    navigator.clipboard.writeText(input.value).then(() => {
        const btn = document.getElementById('copy-secret-btn');
        if (btn) {
            const origText = btn.textContent;
            btn.textContent = '✓ Copied!';
            setTimeout(() => {
                btn.textContent = origText;
            }, 2000);
        }
    });
}

function initializeEditorTabs() {
    const tabElements = document.querySelectorAll('.client-editor-tabs, .api-resource-editor-tabs, .user-details-tabs');

    if (tabElements.length === 0 || !window.customElements) {
        return;
    }

    window.customElements.whenDefined('ck-tabs').then(() => {
        tabElements.forEach(tabElement => {
            const shadowRoot = tabElement.shadowRoot;
            if (!shadowRoot) {
                return;
            }

            if (!shadowRoot.querySelector('[data-panel-height-fix]')) {
                const style = document.createElement('style');
                style.dataset.panelHeightFix = '';
                style.textContent = '.panel[style*="display: none"] { display: block !important; position: absolute !important; visibility: hidden !important; pointer-events: none !important; }';
                shadowRoot.appendChild(style);
            }

            equalizeTabHeights(tabElement);
        });

        let resizeTimeout;
        window.addEventListener('resize', () => {
            clearTimeout(resizeTimeout);
            resizeTimeout = setTimeout(() => tabElements.forEach(equalizeTabHeights), 150);
        });
    });
}

function equalizeTabHeights(tabElement) {
    const panels = tabElement.shadowRoot?.querySelectorAll('.panel');
    if (!panels || panels.length < 2) {
        return;
    }

    panels.forEach(panel => panel.style.removeProperty('min-height'));

    const panelHeights = Array.from(panels, panel => {
        const originalStyle = panel.getAttribute('style');
        const isHidden = getComputedStyle(panel).display === 'none';

        if (isHidden) {
            panel.style.setProperty('display', 'block', 'important');
            panel.style.setProperty('position', 'absolute', 'important');
            panel.style.setProperty('visibility', 'hidden', 'important');
            panel.style.setProperty('pointer-events', 'none', 'important');
        }

        const height = panel.scrollHeight;

        if (isHidden) {
            if (originalStyle === null) {
                panel.removeAttribute('style');
            } else {
                panel.setAttribute('style', originalStyle);
            }
        }

        return height;
    });

    const maximumHeight = Math.max(...panelHeights);
    panels.forEach(panel => panel.style.setProperty('min-height', `${maximumHeight}px`, 'important'));
}
