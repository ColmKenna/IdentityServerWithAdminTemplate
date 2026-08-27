using System.ComponentModel.DataAnnotations;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Clients;

public class ClientCreateInputModel
{
    [Required(ErrorMessage = "Client ID is required")]
    [StringLength(ValidationConstants.MaxClientIdLength, ErrorMessage = "Client ID cannot exceed 200 characters")]
    public string ClientId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Client Name is required")]
    [StringLength(ValidationConstants.MaxNameLength, ErrorMessage = "Client Name cannot exceed 200 characters")]
    public string ClientName { get; set; } = string.Empty;

    [StringLength(ValidationConstants.MaxDescriptionLength, ErrorMessage = "Description cannot exceed 1000 characters")]
    public string? Description { get; set; }

    public string SelectedPreset { get; set; } = "web";
    public bool RequirePkce { get; set; } = true;
    public bool RequireClientSecret { get; set; } = true;
    public List<string> GrantTypes { get; set; } = new();
    public List<string> RedirectUris { get; set; } = new();
    public List<string> PostLogoutRedirectUris { get; set; } = new();
    public List<string> CorsOrigins { get; set; } = new();
    public List<string> AllowedScopes { get; set; } = new();
}

public class ClientCreateResult
{
    public required AdminMutationStatus Status { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? PlaintextSecret { get; init; }
    public string? ClientId { get; init; }
    public IReadOnlyDictionary<string, string[]> Errors { get; init; } = ValidationErrorDictionary.Empty;

    public static ClientCreateResult Failed(string errorMessage, AdminMutationStatus status = AdminMutationStatus.ValidationFailed) =>
        new() { Status = status, Success = false, ErrorMessage = errorMessage, Errors = new ValidationErrorDictionary().AddError(string.Empty, errorMessage) };

    public static ClientCreateResult Failed(string field, string errorMessage, AdminMutationStatus status = AdminMutationStatus.ValidationFailed) =>
        new() { Status = status, Success = false, ErrorMessage = errorMessage, Errors = new ValidationErrorDictionary().AddError(field, errorMessage) };

    public static ClientCreateResult Failed(IReadOnlyDictionary<string, string[]> errors, AdminMutationStatus status = AdminMutationStatus.ValidationFailed) =>
        new() { Status = status, Success = false, ErrorMessage = "Validation failed.", Errors = errors };

    public static ClientCreateResult Succeeded(string clientId, string? plaintextSecret = null) =>
        new() { Status = AdminMutationStatus.Succeeded, Success = true, ClientId = clientId, PlaintextSecret = plaintextSecret };
}

public interface IClientCreateService
{
    Task<ClientCreateResult> CreateClientAsync(ClientCreateInputModel input, CancellationToken cancellationToken = default);
    Task<ClientCreateResult> CloneClientAsync(string sourceClientId, ClientCreateInputModel input, CancellationToken cancellationToken = default);
    Task<List<string>> GetAvailableScopesAsync(CancellationToken cancellationToken = default);
}
