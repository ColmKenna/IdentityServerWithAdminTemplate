function selectPreset(presetKey) {
    document.getElementById('selected-preset-input').value = presetKey;

    document.querySelectorAll('.preset-card').forEach(c => {
        if (c.dataset.preset === presetKey) {
            c.classList.add('active');
        } else {
            c.classList.remove('active');
        }
    });

    const preset = window.availablePresets.find(p => p.id === presetKey);
    if (!preset) return;

    const pkceInput = document.getElementById('input-require-pkce');
    const secretInput = document.getElementById('input-require-secret');
    const authCodeCheck = document.getElementById('grant-auth-code');
    const clientCredsCheck = document.getElementById('grant-client-creds');
    const uriSection = document.getElementById('uri-configuration-section');

    pkceInput.checked = preset.requirePkce;
    secretInput.checked = preset.requireClientSecret;
    authCodeCheck.checked = preset.grantTypes.includes('authorization_code');
    clientCredsCheck.checked = preset.grantTypes.includes('client_credentials');

    if (!preset.grantTypes.includes('authorization_code') && !preset.grantTypes.includes('implicit')) {
        if (uriSection) uriSection.style.display = 'none';
        
        // Uncheck oidc identity scopes
        document.querySelectorAll('.scope-checkbox').forEach(cb => {
            if (cb.value === 'openid' || cb.value === 'profile') {
                cb.checked = false;
            }
        });
    } else { 
        if (uriSection) uriSection.style.display = 'block';
        checkDefaultScopes();
    }
}

function checkDefaultScopes() {
    document.querySelectorAll('.scope-checkbox').forEach(cb => {
        if (cb.value === 'openid' || cb.value === 'profile') {
            cb.checked = true;
        }
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
            setTimeout(() => { btn.textContent = origText; }, 2000);
        }
    });
}

// Initialise preset view state on page load
document.addEventListener('DOMContentLoaded', () => {
    if (!window.availablePresets) return;

    const currentPresetId = document.getElementById('selected-preset-input').value || 'web';
    const preset = window.availablePresets.find(p => p.id === currentPresetId);
    
    if (preset && !preset.grantTypes.includes('authorization_code') && !preset.grantTypes.includes('implicit')) {
        const uriSection = document.getElementById('uri-configuration-section');
        if (uriSection) uriSection.style.display = 'none';
    }
});
