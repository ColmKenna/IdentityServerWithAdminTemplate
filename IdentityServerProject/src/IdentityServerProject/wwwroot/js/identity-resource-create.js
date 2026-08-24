const templateData = {
    profile: {
        name: 'profile',
        displayName: 'User Profile',
        description: 'Your profile information',
        claims: ['sub', 'name', 'family_name', 'given_name', 'middle_name', 'nickname', 'preferred_username', 'profile', 'picture', 'website', 'gender', 'birthdate', 'zoneinfo', 'locale', 'updated_at']
    },
    email: {
        name: 'email',
        displayName: 'Email Address',
        description: 'Your email address',
        claims: ['email', 'email_verified']
    },
    address: {
        name: 'address',
        displayName: 'Address',
        description: 'Your address information',
        claims: ['address']
    },
    phone: {
        name: 'phone',
        displayName: 'Phone Number',
        description: 'Your phone number',
        claims: ['phone_number', 'phone_number_verified']
    }
};

function applyTemplate(templateName) {
    const template = templateData[templateName];
    if (!template) {
        return;
    }

    document.getElementById('resource-name').value = template.name;
    document.getElementById('resource-display-name').value = template.displayName;
    document.getElementById('resource-description').value = template.description;

    updateClaimsDisplay(template.claims);
}

function updateClaimsDisplay(claims) {
    const claimsList = document.getElementById('claims-list');
    const noClaimsMessage = document.getElementById('no-claims-message');
    const form = document.getElementById('create-resource-form');

    if (claims && claims.length > 0) {
        claimsList.innerHTML = claims.map(claim =>
            `<code style="background: #e8ebee; padding: 4px 8px; border-radius: 3px; font-size: 12px;">${claim}</code>`
        ).join('');
        claimsList.style.display = 'block';
        noClaimsMessage.style.display = 'none';

        form.querySelectorAll('input[name="Input.UserClaims"]').forEach(input => input.remove());
        claims.forEach(claim => {
            const input = document.createElement('input');
            input.type = 'hidden';
            input.name = 'Input.UserClaims';
            input.value = claim;
            form.appendChild(input);
        });
    } else {
        claimsList.style.display = 'none';
        noClaimsMessage.style.display = 'block';
        form.querySelectorAll('input[name="Input.UserClaims"]').forEach(input => input.remove());
    }
}

document.querySelectorAll('.preset-card').forEach(card => {
    card.addEventListener('click', function () {
        document.querySelectorAll('.preset-card').forEach(item => item.classList.remove('active'));
        this.classList.add('active');
    });
});
