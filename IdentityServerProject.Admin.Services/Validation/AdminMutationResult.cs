using System.Collections.Generic;
using System.Linq;

namespace IdentityServerProject.Services.Validation;

public enum AdminMutationStatus
{
    Succeeded,
    NotFound,
    ValidationFailed,
    Conflict,
    Denied
}

public sealed record AdminMutationResult(
    AdminMutationStatus Status,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Status == AdminMutationStatus.Succeeded;
    public string? ErrorMessage => Errors.Values.SelectMany(messages => messages).FirstOrDefault();

    public static AdminMutationResult Success() =>
        new(AdminMutationStatus.Succeeded, ValidationErrorDictionary.Empty);

    public static AdminMutationResult NotFoundResult() =>
        new(AdminMutationStatus.NotFound, new ValidationErrorDictionary().AddError(string.Empty, "The requested resource was not found."));

    public static AdminMutationResult ConflictResult(string field, string message) =>
        new(AdminMutationStatus.Conflict, new ValidationErrorDictionary().AddError(field, message));

    public static AdminMutationResult DeniedResult(string field, string message) =>
        new(AdminMutationStatus.Denied, new ValidationErrorDictionary().AddError(field, message));

    public static AdminMutationResult ValidationFailure(IReadOnlyDictionary<string, string[]> errors) =>
        new(AdminMutationStatus.ValidationFailed, errors);

    public static AdminMutationResult ValidationFailure(ValidationErrorDictionary errors) =>
        new(AdminMutationStatus.ValidationFailed, errors);

    public static AdminMutationResult ValidationFailure(string field, string message) =>
        new(AdminMutationStatus.ValidationFailed, new ValidationErrorDictionary().AddError(field, message));
}
