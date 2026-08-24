using System.Collections.Generic;

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
        new(AdminMutationStatus.Succeeded, new Dictionary<string, string[]>());

    public static AdminMutationResult NotFoundResult() =>
        new(AdminMutationStatus.NotFound, new Dictionary<string, string[]>
        {
            [string.Empty] = new[] { "The requested resource was not found." }
        });

    public static AdminMutationResult ConflictResult(string field, string message) =>
        new(AdminMutationStatus.Conflict, new Dictionary<string, string[]> { [field] = new[] { message } });

    public static AdminMutationResult ValidationFailure(IReadOnlyDictionary<string, string[]> errors) =>
        new(AdminMutationStatus.ValidationFailed, errors);

    public static AdminMutationResult ValidationFailure(string field, string message) =>
        new(AdminMutationStatus.ValidationFailed, new Dictionary<string, string[]> { [field] = new[] { message } });
}
