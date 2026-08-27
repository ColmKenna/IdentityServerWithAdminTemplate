namespace IdentityServerProject.Services.IdentityResources;

public class IdentityResourceTemplate
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Claims { get; set; } = new();
}

public static class IdentityResourceTemplates
{
    public static readonly Dictionary<string, IdentityResourceTemplate> Templates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            {
                "profile",
                new IdentityResourceTemplate
                {
                    Name = "profile",
                    DisplayName = "User Profile",
                    Description = "Your profile information",
                    Claims = new List<string>
                    {
                        "sub", "name", "family_name", "given_name", "middle_name", "nickname",
                        "preferred_username", "profile", "picture", "website", "gender",
                        "birthdate", "zoneinfo", "locale", "updated_at"
                    }
                }
            },
            {
                "email",
                new IdentityResourceTemplate
                {
                    Name = "email",
                    DisplayName = "Email Address",
                    Description = "Your email address",
                    Claims = new List<string> { "email", "email_verified" }
                }
            },
            {
                "address",
                new IdentityResourceTemplate
                {
                    Name = "address",
                    DisplayName = "Address",
                    Description = "Your address information",
                    Claims = new List<string> { "address" }
                }
            },
            {
                "phone",
                new IdentityResourceTemplate
                {
                    Name = "phone",
                    DisplayName = "Phone Number",
                    Description = "Your phone number",
                    Claims = new List<string> { "phone_number", "phone_number_verified" }
                }
            }
        };
}