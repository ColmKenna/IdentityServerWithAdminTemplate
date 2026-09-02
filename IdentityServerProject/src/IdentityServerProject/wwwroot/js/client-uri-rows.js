function addUriRow(containerId, inputName, placeholder) {
    const container = document.getElementById(containerId);
    const div = document.createElement('div');
    div.className = 'uri-input-row';
    div.style.display = 'flex';
    div.style.gap = '8px';
    div.style.marginBottom = '8px';

    const label = inputName.includes('PostLogout') ? 'Post-logout redirect URI' :
                  inputName.includes('Cors') ? 'Allowed CORS origin' : 'Redirect URI';

    div.innerHTML = `
        <input type="url" name="${inputName}" class="form-control" placeholder="${placeholder}" aria-label="${label}" />
        <button type="button" class="btn-danger btn-sm" onclick="removeUriRow(this)" aria-label="Remove URI">&times;</button>
    `;
    container.appendChild(div);
}

function removeUriRow(button) {
    const row = button.closest('.uri-input-row');
    if (row) {
        row.remove();
    }
}
