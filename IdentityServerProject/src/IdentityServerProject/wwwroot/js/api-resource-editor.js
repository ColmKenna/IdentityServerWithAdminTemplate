document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('[data-action="open-modal"]').forEach(button => {
        button.addEventListener('click', () => {
            const modal = document.getElementById(button.dataset.modal);
            if (!modal) {
                return;
            }

            const input = modal.querySelector('#revoke-secret-id-input');
            if (input && button.dataset.secretId) {
                input.value = button.dataset.secretId;
            }

            const confirmInput = modal.querySelector('.confirm-input');
            const submitButton = modal.querySelector('[data-confirm-submit]');
            if (confirmInput && submitButton) {
                confirmInput.value = '';
                submitButton.disabled = true;
            }

            modal.showModal();
        });
    });

    document.querySelectorAll('.confirm-input').forEach(input => {
        input.addEventListener('input', () => {
            const dialog = input.closest('dialog');
            const submitButton = dialog?.querySelector('[data-confirm-submit]');
            if (!submitButton) {
                return;
            }

            submitButton.disabled = input.value.trim() !== input.dataset.confirmWord;
        });
    });

    const copyButton = document.getElementById('copy-secret-btn');
    if (copyButton) {
        copyButton.addEventListener('click', () => {
            const target = document.getElementById(copyButton.dataset.copyTarget);
            if (!target) {
                return;
            }

            navigator.clipboard.writeText(target.textContent || '').then(() => {
                copyButton.textContent = 'Copied!';
                setTimeout(() => {
                    copyButton.textContent = 'Copy';
                }, 2000);
            });
        });
    }

    wireQueryStringTabs(document.getElementById('api-resource-editor-tabs'), 'basics');
});
