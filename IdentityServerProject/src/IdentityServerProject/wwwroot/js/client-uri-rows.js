function addUriRow(containerId, inputName, placeholder) {
    const container = document.getElementById(containerId);
    const div = document.createElement('div');
    div.className = 'uri-input-row';
    div.style.display = 'flex';
    div.style.gap = '8px';
    div.style.marginBottom = '8px';

    div.innerHTML = `
        <input type="url" name="${inputName}" class="form-control" placeholder="${placeholder}" />
        <button type="button" class="btn-danger btn-sm" onclick="removeUriRow(this)">&times;</button>
    `;
    container.appendChild(div);
}

function removeUriRow(button) {
    const row = button.closest('.uri-input-row');
    if (row) {
        row.remove();
    }
}
