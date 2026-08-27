using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Apis;

/// <summary>
/// Result of creating or updating API resource basic settings.
/// </summary>
public sealed class SaveApiResourceBasicsResult
{
    public required AdminMutationStatus Status { get; init; }
    public ApiResourceBasicsValidationErrors? ValidationErrors { get; init; }
    public IReadOnlyDictionary<string, string[]> Errors { get; init; } = ValidationErrorDictionary.Empty;

    public bool Succeeded => Status == AdminMutationStatus.Succeeded;
    public bool Success => Succeeded;

    public static SaveApiResourceBasicsResult SucceededResult() => new()
    {
        Status = AdminMutationStatus.Succeeded,
    };

    public static SaveApiResourceBasicsResult NotFoundResult() => new()
    {
        Status = AdminMutationStatus.NotFound,
        Errors = new ValidationErrorDictionary().AddError(string.Empty, "The requested resource was not found.")
    };

    public static SaveApiResourceBasicsResult ConflictResult(string field, string message) => new()
    {
        Status = AdminMutationStatus.Conflict,
        Errors = new ValidationErrorDictionary().AddError(field, message)
    };

    public static SaveApiResourceBasicsResult ValidationFailure(ApiResourceBasicsValidationErrors errors) => new()
    {
        Status = AdminMutationStatus.ValidationFailed,
        ValidationErrors = errors,
        Errors = errors.ToDictionary()
    };

    public static SaveApiResourceBasicsResult ValidationFailure(string field, string message) => new()
    {
        Status = AdminMutationStatus.ValidationFailed,
        Errors = new ValidationErrorDictionary().AddError(field, message)
    };
}
