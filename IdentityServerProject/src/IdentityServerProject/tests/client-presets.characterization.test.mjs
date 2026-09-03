import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';
import {JSDOM} from 'jsdom';

async function loadClientUriRowsScript() {
    const containers = new Map();
    const document = {
        addEventListener() {
        },
        createElement() {
            return {
                className: '',
                style: {},
                innerHTML: '',
                remove() {
                    this.removed = true;
                }
            };
        },
        getElementById(id) {
            return containers.get(id) ?? null;
        },
        querySelectorAll() {
            return [];
        }
    };
    const context = {
        document, window: {}, navigator: {}, setTimeout() {
        }
    };
    const script = await readFile(new URL('../wwwroot/js/client-uri-rows.js', import.meta.url), 'utf8');
    vm.runInNewContext(script, context);

    return {containers, context};
}

test('addUriRow appends a removable URI input using the supplied binding name and placeholder', async () => {
    const {containers, context} = await loadClientUriRowsScript();
    const rows = [];
    containers.set('redirect-uris', {appendChild: row => rows.push(row)});

    context.addUriRow('redirect-uris', 'Input.RedirectUris', 'https://example.test/signin');

    assert.equal(rows.length, 1);
    assert.equal(rows[0].className, 'uri-input-row');
    assert.match(rows[0].innerHTML, /name="Input.RedirectUris"/);
    assert.match(rows[0].innerHTML, /placeholder="https:\/\/example.test\/signin"/);
    assert.match(rows[0].innerHTML, /removeUriRow\(this\)/);
});

test('removeUriRow removes the closest URI row when one exists', async () => {
    const {context} = await loadClientUriRowsScript();
    const row = {
        removed: false, remove() {
            this.removed = true;
        }
    };

    context.removeUriRow({closest: selector => selector === '.uri-input-row' ? row : null});

    assert.equal(row.removed, true);
});

test('selectPreset reads available presets from application/json script element', async () => {
    const script = await readFile(new URL('../wwwroot/js/client-presets.js', import.meta.url), 'utf8');
    const dom = new JSDOM(`<!doctype html><html><body>
        <script id="available-presets-data" type="application/json">
            [{"id":"m2m","requirePkce":false,"requireClientSecret":true,"grantTypes":["client_credentials"]}]
        </script>
        <input id="selected-preset-input" value="web" />
        <button class="preset-card" data-preset="m2m"></button>
        <input id="input-require-pkce" type="checkbox" checked />
        <input id="input-require-secret" type="checkbox" />
        <input id="grant-auth-code" type="checkbox" checked />
        <input id="grant-client-creds" type="checkbox" />
        <div id="uri-configuration-section"></div>
    </body></html>`, {runScripts: 'outside-only'});

    dom.window.eval(script);
    dom.window.selectPreset('m2m');

    assert.equal(dom.window.document.getElementById('selected-preset-input').value, 'm2m');
    assert.equal(dom.window.document.getElementById('input-require-pkce').checked, false);
    assert.equal(dom.window.document.getElementById('input-require-secret').checked, true);
    assert.equal(dom.window.document.getElementById('grant-auth-code').checked, false);
    assert.equal(dom.window.document.getElementById('grant-client-creds').checked, true);
    assert.equal(dom.window.document.getElementById('uri-configuration-section').style.display, 'none');
    assert.equal(dom.window.document.querySelector('.preset-card').classList.contains('active'), true);
});

