document.addEventListener('DOMContentLoaded', () => {
    const modal = document.getElementById('delete-resource-modal');
    const nameInput = document.getElementById('delete-resource-modal-name-input');
    const confirmMessage = document.getElementById('delete-resource-modal-message');
    const blockedMessage = document.getElementById('delete-resource-modal-blocked-message');
    const nameText = document.getElementById('delete-resource-modal-name-text');
    const blockedNameText = document.getElementById('delete-resource-modal-blocked-name-text');
    const blockedCount = document.getElementById('delete-resource-modal-blocked-count');
    const confirmButton = document.getElementById('delete-resource-modal-confirm');

    document.querySelectorAll('[data-action="delete-resource"]').forEach(button => {
        button.addEventListener('click', () => {
            const name = button.getAttribute('data-delete-name');
            const referenceCount = parseInt(button.getAttribute('data-reference-count') || '0', 10);
            const isNonEditable = button.getAttribute('data-non-editable') === 'true';

            nameInput.value = name;

            if (referenceCount > 0 || isNonEditable) {
                blockedNameText.textContent = name;
                blockedCount.textContent = referenceCount;
                blockedMessage.hidden = false;
                confirmMessage.hidden = true;
                confirmButton.style.display = 'none';
            } else {
                nameText.textContent = name;
                confirmMessage.hidden = false;
                blockedMessage.hidden = true;
                confirmButton.style.display = '';
            }

            if (typeof modal.showModal === 'function') {
                modal.showModal();
            }
        });
    });
});
