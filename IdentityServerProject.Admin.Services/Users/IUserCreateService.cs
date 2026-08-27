using System.ComponentModel.DataAnnotations;

namespace IdentityServerProject.Services.Users;

public class UserCreateInputModel
{
    [Required(ErrorMessage = "Username is required")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Enter a valid email address")]
    public string Email { get; set; } = string.Empty;

    public string? FullName { get; set; }

    [Required(ErrorMessage = "Password is required")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm the password")]
    [Compare(nameof(Password), ErrorMessage = "Password and confirmation password do not match")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class UserCreateResult
{
    public bool Success { get; set; }
    public string? UserId { get; set; }
    public List<string> Errors { get; set; } = new();

    public static UserCreateResult Succeeded(string userId) =>
        new() { Success = true, UserId = userId };

    public static UserCreateResult Failed(List<string> errors) =>
        new() { Success = false, Errors = errors };
}

/// <summary>
/// Provisions new ASP.NET Core Identity user accounts for the Admin console.
/// </summary>
public interface IUserCreateService
{
    /// <summary>
    /// Creates a new user account with validated credentials via UserManager.CreateAsync.
    /// Returns Success = false with Identity's validation errors (e.g. duplicate username/email,
    /// password policy violations) when creation fails.
    /// </summary>
    Task<UserCreateResult> CreateUserAsync(UserCreateInputModel input, CancellationToken cancellationToken = default);
}
