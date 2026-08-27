using Microsoft.AspNetCore.Identity;

namespace IdentityServerProject.Data;

public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
}