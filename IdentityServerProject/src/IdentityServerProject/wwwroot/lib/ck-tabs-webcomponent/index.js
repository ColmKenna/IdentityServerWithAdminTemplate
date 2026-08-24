(function (global, factory) {
    typeof exports === 'object' && typeof module !== 'undefined' ? factory(exports) :
    typeof define === 'function' && define.amd ? define(['exports'], factory) :
    (global = typeof globalThis !== 'undefined' ? globalThis : global || self, factory(global["WebComponentLibrary/ck-tabs"] = {}));
})(this, (function (exports) { 'use strict';

    /**
     * Individual tab element that holds tab content and label
     */
    class CKTab extends HTMLElement {
        constructor() {
            super();
        }
        connectedCallback() {
            // Tab content is handled by the parent ck-tabs component
        }
        /**
         * Gets the label text for this tab
         */
        get label() {
            return this.getAttribute('label') || 'Tab';
        }
        /**
         * Sets the label text for this tab
         */
        set label(value) {
            this.setAttribute('label', value);
        }
        /**
         * Gets whether this tab is currently active
         */
        get active() {
            return this.hasAttribute('active');
        }
        /**
         * Sets whether this tab is currently active
         */
        set active(value) {
            if (value) {
                this.setAttribute('active', '');
            }
            else {
                this.removeAttribute('active');
            }
        }
    }
    /**
     * Container element that manages tab state and presentation
     */
    class CKTabs extends HTMLElement {
        constructor() {
            super();
            this.currentActiveIndex = 0;
            this.shadow = this.attachShadow({ mode: 'open' });
        }
        connectedCallback() {
            this.render();
            this.setupEventListeners();
        }
        /**
         * Creates and applies styles using Constructable Stylesheet Pattern with fallback
         */
        createStyles() {
            const cssText = `
      .tab-headings {
        display: flex;
        flex-direction: row;
        overflow-x: overlay;
        width: 100%;
        border-bottom: 1px solid var(--tab-border, #e0e0e0);
        background: var(--tab-bg, #ffffff);
      }
      :host {
        display: flex;
        flex-wrap: wrap;
        background: var(--tab-bg, #ffffff);
        border: 1px solid var(--tab-border, #e0e0e0);
        border-radius: 8px;
        align-items: stretch;
        width: 80%;
        margin: 0 auto;
        overflow: hidden;
        font-family: var(--tab-font-family, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif);
      }

      .tab-heading {
        width: 100%;
        padding: 20px 30px;
        background: var(--tab-bg, #ffffff);
        cursor: pointer;
        font-weight: bold;
        font-size: 18px;
        color: var(--tab-text, #333333);
        transition: background 0.1s, color 0.1s;
        border: none;
        text-align: left;
        font-family: inherit;
        border-bottom: 1px solid var(--tab-border, #e0e0e0);
      }

      .tab-heading:hover {
        background: var(--tab-hover-bg, #f5f5f5);
      }

      .tab-heading:active {
        background: var(--primary-color, #007bff);
        color: var(--text-light, #ffffff);
      }

      .tab-heading:focus {
        outline: 2px solid var(--focus-color, #007bff);
        outline-offset: -2px;
      }

      .tab-heading.active {
        background: var(--primary-hover, #0056b3);
        color: var(--text-light, #ffffff);
        border-bottom: 3px solid var(--primary-color, #007bff);
      }

      .panel {
        display: none;
        background: var(--tab-content-bg, #ffffff);
        width: 100%;
        height: 450px;
        overflow: hidden;
        box-sizing: border-box;
      }

      .panel.active {
        display: block;
      }

      .panel-content {
        width: 100%;
        height: 100%;
        padding: 20px 30px 30px;
        overflow-x: hidden;
        overflow-y: auto;
        color: var(--tab-content-text, #333333);
        box-sizing: border-box;
      }

      .panel-content h1 {
        margin: 0;
        font-family: var(--heading-font-family, serif);
        font-size: 36px;
        color: var(--tab-content-text, #333333);
      }

      .panel-content p,
      .panel-content ul {
        margin-top: 20px;
        margin-bottom: 0;
        padding: 0;
        font-size: 20px;
        line-height: 1.5;
        color: var(--tab-content-text, #333333);
      }

      .panel-content ul {
        padding-left: 20px;
      }

      .panel-content li {
        margin-bottom: 10px;
      }

      /* Custom scrollbar for panel content */
      .panel-content::-webkit-scrollbar {
        width: 8px;
      }

      .panel-content::-webkit-scrollbar-track {
        background: var(--scrollbar-track, #f1f1f1);
        border-radius: 4px;
      }

      .panel-content::-webkit-scrollbar-thumb {
        background: var(--scrollbar-thumb, #c1c1c1);
        border-radius: 4px;
      }

      .panel-content::-webkit-scrollbar-thumb:hover {
        background: var(--primary-hover, #0056b3);
      }

      /* Responsive design for desktop */
      @media (min-width: 600px) {
        .tab-heading {
          width: auto;
          border-bottom: none;
          border-radius: 8px 8px 0 0;
        }

        .tab-heading:hover {
          background: var(--tab-hover-bg, #f5f5f5);
        }

        .tab-heading.active {
          background: var(--primary-hover, #0056b3);
          color: var(--text-light, #ffffff);
          border-bottom: 3px solid var(--primary-color, #007bff);
        }

        .panel {
          order: 99;
          border-bottom-left-radius: 8px;
          border-bottom-right-radius: 8px;
        }
      }

      /* High contrast mode support */
      @media (prefers-contrast: high) {
        :host {
          border: 2px solid;
        }
        
        .tab-heading {
          border: 1px solid;
        }
      }

      /* Reduced motion support */
      @media (prefers-reduced-motion: reduce) {
        .tab-heading {
          transition: none;
        }
      }
    `;
            // Try to use Constructable Stylesheet Pattern for modern browsers
            if ('replaceSync' in CSSStyleSheet.prototype) {
                try {
                    if (!this.styleSheet) {
                        this.styleSheet = new CSSStyleSheet();
                    }
                    this.styleSheet.replaceSync(cssText);
                    this.shadow.adoptedStyleSheets = [this.styleSheet];
                    return '';
                }
                catch (error) {
                    console.warn('Constructable Stylesheets not supported, falling back to style element');
                }
            }
            // Fallback for Safari and older browsers
            return `<style>${cssText}</style>`;
        }
        /**
         * Renders the tab component with all tabs and panels
         */
        render() {
            const tabs = Array.from(this.querySelectorAll('ck-tab'));
            let activeIndex = tabs.findIndex((tab) => tab.active);
            if (activeIndex === -1)
                activeIndex = 0;
            this.currentActiveIndex = activeIndex;
            // Update the active state on all tab elements
            tabs.forEach((tab, index) => {
                tab.active = index === activeIndex;
            });
            const styleElement = this.createStyles();
            const headingsHtml = tabs
                .map((tab, index) => `
      <button 
        class="tab-heading ${index === activeIndex ? 'active' : ''}" 
        data-index="${index}"
        role="tab"
        aria-selected="${index === activeIndex}"
        aria-controls="panel-${index}"
        id="tab-${index}"
        type="button"
      >
        ${this.escapeHtml(tab.label)}
      </button>
      `)
                .join('');
            const panelsHtml = tabs
                .map((tab, index) => `
      <div 
        class="panel ${index === activeIndex ? 'active' : ''}"
        role="tabpanel"
        aria-labelledby="tab-${index}"
        id="panel-${index}"
        ${index !== activeIndex ? 'aria-hidden="true"' : ''}
      >
        <div class="panel-content">
        <slot name="tab-${index}"></slot>
        </div>
      </div>
      `)
                .join('');
            this.shadow.innerHTML = `
      ${styleElement}
      <div class="tab-headings">${headingsHtml}</div>
      ${panelsHtml}
    `;
            // Assign slot names to tabs
            tabs.forEach((tab, index) => {
                tab.setAttribute('slot', `tab-${index}`);
            });
        }
        /**
         * Sets up event listeners for tab interaction
         */
        setupEventListeners() {
            this.shadow.addEventListener('click', (e) => {
                const target = e.target;
                if (target.classList.contains('tab-heading')) {
                    const index = parseInt(target.dataset.index || '0');
                    this.setActiveTab(index);
                }
            });
            // Keyboard navigation support
            this.shadow.addEventListener('keydown', (e) => {
                const keyEvent = e;
                const target = keyEvent.target;
                if (target.classList.contains('tab-heading')) {
                    const index = parseInt(target.dataset.index || '0');
                    const tabs = this.shadow.querySelectorAll('.tab-heading');
                    switch (keyEvent.key) {
                        case 'ArrowLeft':
                        case 'ArrowUp': {
                            keyEvent.preventDefault();
                            const prevIndex = index > 0 ? index - 1 : tabs.length - 1;
                            tabs[prevIndex].focus();
                            this.setActiveTab(prevIndex);
                            break;
                        }
                        case 'ArrowRight':
                        case 'ArrowDown': {
                            keyEvent.preventDefault();
                            const nextIndex = index < tabs.length - 1 ? index + 1 : 0;
                            tabs[nextIndex].focus();
                            this.setActiveTab(nextIndex);
                            break;
                        }
                        case 'Home': {
                            keyEvent.preventDefault();
                            tabs[0].focus();
                            this.setActiveTab(0);
                            break;
                        }
                        case 'End': {
                            keyEvent.preventDefault();
                            tabs[tabs.length - 1].focus();
                            this.setActiveTab(tabs.length - 1);
                            break;
                        }
                    }
                }
            });
        }
        /**
         * Sets the active tab by index
         */
        setActiveTab(index) {
            const tabs = this.querySelectorAll('ck-tab');
            const headings = this.shadow.querySelectorAll('.tab-heading');
            const panels = this.shadow.querySelectorAll('.panel');
            const previousActiveIndex = this.currentActiveIndex;
            // Remove active state from all elements
            headings.forEach((h) => {
                h.classList.remove('active');
                h.setAttribute('aria-selected', 'false');
            });
            panels.forEach((p) => {
                p.classList.remove('active');
                p.setAttribute('aria-hidden', 'true');
            });
            tabs.forEach((t) => t.removeAttribute('active'));
            // Set active state for selected elements
            if (headings[index]) {
                headings[index].classList.add('active');
                headings[index].setAttribute('aria-selected', 'true');
            }
            if (panels[index]) {
                panels[index].classList.add('active');
                panels[index].removeAttribute('aria-hidden');
            }
            if (tabs[index]) {
                tabs[index].setAttribute('active', '');
            }
            this.currentActiveIndex = index;
            // Dispatch custom events
            this.dispatchEvent(new CustomEvent('tab-change', {
                detail: {
                    activeIndex: index,
                    activeTab: tabs[index],
                    previousActiveIndex,
                    previousActiveTab: tabs[previousActiveIndex],
                },
                bubbles: true,
            }));
            this.dispatchEvent(new CustomEvent('tab-selected', {
                detail: {
                    selectedIndex: index,
                    selectedTab: tabs[index],
                    tabLabel: tabs[index] ? tabs[index].label : '',
                    tabElement: this,
                    timestamp: Date.now(),
                },
                bubbles: true,
            }));
        }
        /**
         * Escapes HTML to prevent XSS
         */
        escapeHtml(text) {
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }
        // Public API methods
        /**
         * Gets the currently active tab element
         */
        getActiveTab() {
            const tabs = this.querySelectorAll('ck-tab');
            return tabs[this.currentActiveIndex];
        }
        /**
         * Gets the index of the currently active tab
         */
        getActiveIndex() {
            return this.currentActiveIndex;
        }
        /**
         * Activates a tab by index
         */
        activateTab(index) {
            const tabs = this.querySelectorAll('ck-tab');
            if (index >= 0 && index < tabs.length) {
                this.setActiveTab(index);
            }
        }
        /**
         * Adds a new tab programmatically
         */
        addTab(label, content, active = false) {
            const tab = document.createElement('ck-tab');
            tab.label = label;
            tab.innerHTML = content;
            if (active) {
                // Deactivate all existing tabs first
                const existingTabs = Array.from(this.querySelectorAll('ck-tab'));
                existingTabs.forEach((existingTab) => {
                    existingTab.active = false;
                });
                tab.active = true;
            }
            this.appendChild(tab);
            this.render();
            return tab;
        }
        /**
         * Removes a tab by index
         */
        removeTab(index) {
            const tabs = this.querySelectorAll('ck-tab');
            if (index >= 0 && index < tabs.length) {
                tabs[index].remove();
                // If we removed the active tab, activate the first available tab
                if (index === this.currentActiveIndex && tabs.length > 1) {
                    this.currentActiveIndex = Math.min(index, tabs.length - 2);
                }
                this.render();
                return true;
            }
            return false;
        }
    }
    // Register the custom elements
    if (!customElements.get('ck-tab')) {
        customElements.define('ck-tab', CKTab);
    }
    if (!customElements.get('ck-tabs')) {
        customElements.define('ck-tabs', CKTabs);
    }

    exports.CKTab = CKTab;
    exports.CKTabs = CKTabs;

}));
//# sourceMappingURL=index.js.map
