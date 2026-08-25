using System.Collections.Generic;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Apis;

/// <summary>
/// Result of generating a new API resource secret. <see cref="PlaintextSecret"/> is the
/// only time the raw secret value is ever available — it is hashed before storage and
/// cannot be recovered afterwards.
/// </summary>
public sealed class ApiResourceAddSecretResult
{
    public required AdminMutationStatus Status { get; init; }
    public required bool Success { get; init; }
    public required string? PlaintextSecret { get; init; }
    public IReadOnlyDictionary<string, string[]> Errors { get; init; } = ValidationErrorDictionary.Empty;

    public static ApiResourceAddSecretResult NotFound { get; } = new()
    {
        Status = AdminMutationStatus.NotFound,
        Success = false,
        PlaintextSecret = null
    };

    public static ApiResourceAddSecretResult Succeeded(string plaintextSecret) => new()
    {
        Status = AdminMutationStatus.Succeeded,
        Success = true,
        PlaintextSecret = plaintextSecret
    };

    public static ApiResourceAddSecretResult ValidationFailure(IReadOnlyDictionary<string, string[]> errors) => new()
    {
        Status = AdminMutationStatus.ValidationFailed,
        Success = false,
        PlaintextSecret = null,
        Errors = errors
    };

    public static ApiResourceAddSecretResult ValidationFailure(string field, string message) => new()
    {
        Status = AdminMutationStatus.ValidationFailed,
        Success = false,
        PlaintextSecret = null,
        Errors = new ValidationErrorDictionary().AddError(field, message)
    };
}
