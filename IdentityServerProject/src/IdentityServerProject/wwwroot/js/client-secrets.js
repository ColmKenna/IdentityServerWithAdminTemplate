document.addEventListener('DOMContentLoaded', function () {
    const modal = document.getElementById('revoke-secret-modal');
    const idInput = document.getElementById('revoke-secret-modal-id-input') || document.getElementById('revoke-secret-id-input');
    const descriptionText = document.getElementById('revoke-secret-modal-description-text');

    if (!modal) {
        return;
    }

    document.querySelectorAll('[data-action="revoke-secret"]').forEach(btn => {
        btn.addEventListener('click', function () {
            const secretId = this.getAttribute('data-secret-id');
            const description = this.getAttribute('data-secret-description');

            if (idInput) {
                idInput.value = secretId;
            }
            if (descriptionText) {
                descriptionText.textContent = description;
            }

            if (typeof modal.showModal === 'function') {
                modal.showModal();
            }
        });
    });

    document.querySelectorAll('[data-action="close-modal"]').forEach(btn => {
        btn.addEventListener('click', function () {
            if (typeof modal.close === 'function') {
                modal.close();
            }
        });
    });
});
