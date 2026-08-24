/**
 * Individual tab element that holds tab content and label
 */
export declare class CKTab extends HTMLElement {
    constructor();
    connectedCallback(): void;
    /**
     * Gets the label text for this tab
     */
    get label(): string;
    /**
     * Sets the label text for this tab
     */
    set label(value: string);
    /**
     * Gets whether this tab is currently active
     */
    get active(): boolean;
    /**
     * Sets whether this tab is currently active
     */
    set active(value: boolean);
}
/**
 * Container element that manages tab state and presentation
 */
export declare class CKTabs extends HTMLElement {
    private shadow;
    private currentActiveIndex;
    private styleSheet?;
    constructor();
    connectedCallback(): void;
    /**
     * Creates and applies styles using Constructable Stylesheet Pattern with fallback
     */
    private createStyles;
    /**
     * Renders the tab component with all tabs and panels
     */
    private render;
    /**
     * Sets up event listeners for tab interaction
     */
    private setupEventListeners;
    /**
     * Sets the active tab by index
     */
    private setActiveTab;
    /**
     * Escapes HTML to prevent XSS
     */
    private escapeHtml;
    /**
     * Gets the currently active tab element
     */
    getActiveTab(): CKTab | undefined;
    /**
     * Gets the index of the currently active tab
     */
    getActiveIndex(): number;
    /**
     * Activates a tab by index
     */
    activateTab(index: number): void;
    /**
     * Adds a new tab programmatically
     */
    addTab(label: string, content: string, active?: boolean): CKTab;
    /**
     * Removes a tab by index
     */
    removeTab(index: number): boolean;
}
