function confirmDelete(form) {
    const input = form.querySelector('#deleteConfirmInput');
    if (input.value !== 'DELETE') {
        alert('You must type DELETE to confirm.');
        return false;
    }

    return true;
}

document.addEventListener('DOMContentLoaded', () => {
    wireQueryStringTabs(document.getElementById('user-details-tabs'), 'overview');
});
