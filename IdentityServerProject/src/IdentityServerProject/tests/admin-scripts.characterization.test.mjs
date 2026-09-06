import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import test from 'node:test';
import {JSDOM} from 'jsdom';

const projectRoot = new URL('../', import.meta.url);

async function readProjectFile(relativePath) {
    return readFile(new URL(relativePath, projectRoot), 'utf8');
}

function createDom(body, url = 'https://admin.test/Admin') {
    return new JSDOM(`<!doctype html><html><body>${body}</body></html>`, {
        url,
        runScripts: 'outside-only'
    });
}

async function executeDomReadyScript(dom, script) {
    const ready = new Promise(resolve => {
        dom.window.document.addEventListener('DOMContentLoaded', resolve, {once: true});
    });

    dom.window.eval(script);
    await ready;
    await new Promise(resolve => dom.window.setTimeout(resolve, 0));
}

test('admin shell persists sidebar state', async () => {
    const script = await readProjectFile('wwwroot/js/admin.js');
    const dom = createDom(`<button id="burger"></button>`);

    await executeDomReadyScript(dom, script);

    dom.window.document.getElementById('burger').click();
    assert.equal(dom.window.document.body.classList.contains('collapsed'), true);
    assert.equal(dom.window.localStorage.getItem('admin_sidebar_collapsed'), 'true');
});

test('admin shell still toggles when localStorage is unavailable', async () => {
    const script = await readProjectFile('wwwroot/js/admin.js');
    const dom = createDom('<button id="burger"></button>');
    Object.defineProperty(dom.window, 'localStorage', {
        configurable: true,
        get() {
            throw new Error('Storage unavailable');
        }
    });

    await executeDomReadyScript(dom, script);
    assert.doesNotThrow(() => dom.window.document.getElementById('burger').click());
    assert.equal(dom.window.document.body.classList.contains('collapsed'), true);
});

const userMenuMarkup = `
    <div class="user-menu" id="userMenu">
        <button id="userBtn" aria-label="Account menu" aria-expanded="false" aria-controls="userMenuDropdown"></button>
        <div class="dropdown" id="userMenuDropdown">
            <button type="submit" class="logout">Sign Out</button>
        </div>
    </div>
    <a href="/Admin/Users" id="outside">Users</a>
`;

async function openUserMenu() {
    const script = await readProjectFile('wwwroot/js/admin.js');
    const dom = createDom(userMenuMarkup);

    await executeDomReadyScript(dom, script);

    const {document} = dom.window;
    document.getElementById('userBtn').click();

    assert.equal(document.getElementById('userMenu').classList.contains('open'), true);
    assert.equal(document.getElementById('userBtn').getAttribute('aria-expanded'), 'true');

    return dom;
}

test('user menu toggle keeps aria-expanded in step with the open class', async () => {
    const dom = await openUserMenu();
    const {document} = dom.window;

    document.getElementById('userBtn').click();

    assert.equal(document.getElementById('userMenu').classList.contains('open'), false);
    assert.equal(document.getElementById('userBtn').getAttribute('aria-expanded'), 'false');
});

test('clicking outside the user menu closes it and reports it closed', async () => {
    const dom = await openUserMenu();
    const {document} = dom.window;

    document.getElementById('outside').click();

    assert.equal(document.getElementById('userMenu').classList.contains('open'), false);
    assert.equal(document.getElementById('userBtn').getAttribute('aria-expanded'), 'false');
});

test('clicking inside the open user menu leaves it open', async () => {
    const dom = await openUserMenu();
    const {document} = dom.window;

    document.querySelector('#userMenuDropdown .logout').click();

    assert.equal(document.getElementById('userMenu').classList.contains('open'), true);
    assert.equal(document.getElementById('userBtn').getAttribute('aria-expanded'), 'true');
});

test('escape closes the user menu and returns focus to the toggle', async () => {
    const dom = await openUserMenu();
    const {document, KeyboardEvent} = dom.window;

    document.querySelector('#userMenuDropdown .logout').focus();
    document.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape', bubbles: true}));

    assert.equal(document.getElementById('userMenu').classList.contains('open'), false);
    assert.equal(document.getElementById('userBtn').getAttribute('aria-expanded'), 'false');
    assert.equal(document.activeElement, document.getElementById('userBtn'));
});

test('escape with the user menu already closed leaves focus where it is', async () => {
    const script = await readProjectFile('wwwroot/js/admin.js');
    const dom = createDom(userMenuMarkup);

    await executeDomReadyScript(dom, script);

    const {document, KeyboardEvent} = dom.window;
    const outside = document.getElementById('outside');
    outside.focus();

    document.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape', bubbles: true}));

    assert.equal(document.getElementById('userBtn').getAttribute('aria-expanded'), 'false');
    assert.equal(document.activeElement, outside);
});

test('admin shell populates scope deletion dialog and closes generic dialogs', async () => {
    const script = await readProjectFile('wwwroot/js/admin.js');
    const dom = createDom(`
        <button data-action="delete-scope" data-delete-name="sales.read" data-reference-count="2"></button>
        <dialog id="delete-scope-modal">
            <input id="delete-scope-modal-name-input" />
            <span id="delete-scope-modal-name-text"></span>
            <span id="delete-scope-modal-blocked-name-text"></span>
            <span id="delete-scope-modal-blocked-count"></span>
            <div id="delete-scope-modal-message"></div>
            <div id="delete-scope-modal-blocked-message" hidden></div>
            <button id="delete-scope-modal-confirm"></button>
            <button data-action="close-modal"></button>
        </dialog>
    `);
    const dialog = dom.window.document.querySelector('dialog');
    let opened = false;
    let closed = false;
    dialog.showModal = () => {
        opened = true;
    };
    dialog.close = () => {
        closed = true;
    };

    await executeDomReadyScript(dom, script);
    dom.window.document.querySelector('[data-action="delete-scope"]').click();

    assert.equal(opened, true);
    assert.equal(dom.window.document.getElementById('delete-scope-modal-name-input').value, 'sales.read');
    assert.equal(dom.window.document.getElementById('delete-scope-modal-blocked-count').textContent, '2');
    assert.equal(dom.window.document.getElementById('delete-scope-modal-confirm').disabled, true);

    dom.window.document.querySelector('[data-action="close-modal"]').click();
    assert.equal(closed, true);
});

test('user details tabs update browser history and follow popstate navigation', async () => {
    const tabQueryScript = await readProjectFile('wwwroot/js/tab-query-sync.js');
    const script = await readProjectFile('wwwroot/js/user-details.js');
    const dom = createDom(`
        <ck-tabs id="user-details-tabs">
            <ck-tab data-tab="overview"></ck-tab>
            <ck-tab data-tab="roles"></ck-tab>
            <ck-tab data-tab="claims"></ck-tab>
        </ck-tabs>
    `, 'https://admin.test/Admin/Users/Details/user-1?tab=roles');
    dom.window.customElements.define('ck-tabs', class extends dom.window.HTMLElement {
    });
    const tabs = dom.window.document.getElementById('user-details-tabs');
    let activatedTabIndex = null;
    tabs.activateTab = index => {
        activatedTabIndex = index;
    };

    dom.window.eval(tabQueryScript);
    await executeDomReadyScript(dom, script);
    assert.equal(activatedTabIndex, null);

    const claimsTab = tabs.querySelector('[data-tab="claims"]');
    tabs.dispatchEvent(new dom.window.CustomEvent('tab-selected', {
        detail: {selectedTab: claimsTab}
    }));
    assert.equal(dom.window.location.search, '?tab=claims');

    dom.window.history.replaceState({}, '', '?tab=roles');
    dom.window.dispatchEvent(new dom.window.PopStateEvent('popstate'));
    assert.equal(activatedTabIndex, 1);
});

test('API editor modal carries the selected secret id and enforces exact confirmation', async () => {
    const tabQueryScript = await readProjectFile('wwwroot/js/tab-query-sync.js');
    const script = await readProjectFile('wwwroot/js/api-resource-editor.js');
    const dom = createDom(`
        <button data-action="open-modal" data-modal="revoke-dialog" data-secret-id="17"></button>
        <dialog id="revoke-dialog">
            <input id="revoke-secret-id-input" />
            <input class="confirm-input" data-confirm-word="REVOKE" value="stale" />
            <button data-confirm-submit></button>
        </dialog>
    `);
    const dialog = dom.window.document.querySelector('dialog');
    let opened = false;
    dialog.showModal = () => {
        opened = true;
    };

    dom.window.eval(tabQueryScript);
    await executeDomReadyScript(dom, script);
    dom.window.document.querySelector('[data-action="open-modal"]').click();

    const confirmation = dialog.querySelector('.confirm-input');
    const submit = dialog.querySelector('[data-confirm-submit]');
    assert.equal(opened, true);
    assert.equal(dialog.querySelector('#revoke-secret-id-input').value, '17');
    assert.equal(confirmation.value, '');
    assert.equal(submit.disabled, true);

    confirmation.value = 'REVOKE';
    confirmation.dispatchEvent(new dom.window.Event('input', {bubbles: true}));
    assert.equal(submit.disabled, false);
});

test('grant revocation dialog copies the selected grant details', async () => {
    const script = await readProjectFile('wwwroot/js/grant-list.js');
    const dom = createDom(`
        <button data-action="revoke-grant" data-grant-key="grant-1" data-client-name="Sales" data-subject-id="user-1"></button>
        <dialog id="revoke-grant-modal">
            <input id="revoke-grant-modal-key-input" />
            <span id="revoke-grant-modal-key-text"></span>
            <span id="revoke-grant-modal-client-text"></span>
            <span id="revoke-grant-modal-subject-text"></span>
        </dialog>
    `);
    const dialog = dom.window.document.querySelector('dialog');
    let opened = false;
    dialog.showModal = () => {
        opened = true;
    };

    await executeDomReadyScript(dom, script);
    dom.window.document.querySelector('[data-action="revoke-grant"]').click();

    assert.equal(opened, true);
    assert.equal(dom.window.document.getElementById('revoke-grant-modal-key-input').value, 'grant-1');
    assert.equal(dom.window.document.getElementById('revoke-grant-modal-client-text').textContent, 'Sales');
    assert.equal(dom.window.document.getElementById('revoke-grant-modal-subject-text').textContent, 'user-1');
});

test('identity resource dialog blocks deletion for referenced resources', async () => {
    const script = await readProjectFile('wwwroot/js/identity-resource-list.js');
    const dom = createDom(`
        <button data-action="delete-resource" data-delete-name="profile" data-reference-count="3" data-non-editable="false"></button>
        <dialog id="delete-resource-modal">
            <input id="delete-resource-modal-name-input" />
            <div id="delete-resource-modal-message"></div>
            <div id="delete-resource-modal-blocked-message" hidden></div>
            <span id="delete-resource-modal-name-text"></span>
            <span id="delete-resource-modal-blocked-name-text"></span>
            <span id="delete-resource-modal-blocked-count"></span>
            <button id="delete-resource-modal-confirm"></button>
        </dialog>
    `);
    const dialog = dom.window.document.querySelector('dialog');
    dialog.showModal = () => {
    };

    await executeDomReadyScript(dom, script);
    dom.window.document.querySelector('[data-action="delete-resource"]').click();

    assert.equal(dom.window.document.getElementById('delete-resource-modal-name-input').value, 'profile');
    assert.equal(dom.window.document.getElementById('delete-resource-modal-blocked-message').hidden, false);
    assert.equal(dom.window.document.getElementById('delete-resource-modal-message').hidden, true);
    assert.equal(dom.window.document.getElementById('delete-resource-modal-confirm').style.display, 'none');
});

test('identity resource templates populate fields, claims, and model-binding inputs', async () => {
    const script = await readProjectFile('wwwroot/js/identity-resource-create.js');
    const dom = createDom(`
        <form id="create-resource-form">
            <input id="resource-name" />
            <input id="resource-display-name" />
            <textarea id="resource-description"></textarea>
            <input id="Input_UserClaims" />
        </form>
        <button class="preset-card" data-template="email"></button>
        <div id="claims-container"><div id="claims-list"></div><div id="no-claims-message"></div></div>
    `);

    dom.window.eval(script);
    dom.window.applyTemplate('email');

    assert.equal(dom.window.document.getElementById('resource-name').value, 'email');
    assert.equal(dom.window.document.getElementById('resource-display-name').value, 'Email Address');
    assert.equal(dom.window.document.getElementById('claims-list').textContent, 'emailemail_verified');
    assert.deepEqual(
        [...dom.window.document.querySelectorAll('input[name="Input.UserClaims"]')].map(input => input.value),
        ['email', 'email_verified']);

    const card = dom.window.document.querySelector('.preset-card');
    card.click();
    assert.equal(card.classList.contains('active'), true);
});
