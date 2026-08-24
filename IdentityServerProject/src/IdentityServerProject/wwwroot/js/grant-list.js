document.addEventListener('DOMContentLoaded', () => {
    const modal = document.getElementById('revoke-grant-modal');
    const keyInput = document.getElementById('revoke-grant-modal-key-input');
    const keyText = document.getElementById('revoke-grant-modal-key-text');
    const clientText = document.getElementById('revoke-grant-modal-client-text');
    const subjectText = document.getElementById('revoke-grant-modal-subject-text');

    document.querySelectorAll('[data-action="revoke-grant"]').forEach(button => {
        button.addEventListener('click', () => {
            const key = button.getAttribute('data-grant-key');
            const clientName = button.getAttribute('data-client-name');
            const subjectId = button.getAttribute('data-subject-id');

            keyInput.value = key;
            keyText.textContent = key;
            clientText.textContent = clientName;
            subjectText.textContent = subjectId;

            if (typeof modal.showModal === 'function') {
                modal.showModal();
            }
        });
    });
});
