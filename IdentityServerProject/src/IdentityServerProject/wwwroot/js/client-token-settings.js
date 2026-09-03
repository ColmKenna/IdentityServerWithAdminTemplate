document.addEventListener('DOMContentLoaded', function () {
    const checkbox = document.getElementById('allow-offline-access');
    const panel = document.getElementById('refresh-token-settings');

    function togglePanel() {
        if (!checkbox || !panel) {
            return;
        }
        panel.style.display = checkbox.checked ? 'block' : 'none';
    }

    if (checkbox && panel) {
        checkbox.addEventListener('change', togglePanel);
        togglePanel();
    }
});
