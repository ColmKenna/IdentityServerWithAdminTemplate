using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace IdentityServerProject.Services.Apis;

/// <summary>
///     Strongly typed validation error container for API resource basic settings.
/// </summary>
public sealed class ApiResourceBasicsValidationErrors
{
    public List<string> Name { get; } = new();
    public List<string> DisplayName { get; } = new();
    public List<string> Description { get; } = new();

    public bool HasErrors => Name.Count > 0 || DisplayName.Count > 0 || Description.Count > 0;
    public bool IsValid => !HasErrors;

    public void AddNameError(string error) => Name.Add(error);
    public void AddDisplayNameError(string error) => DisplayName.Add(error);
    public void AddDescriptionError(string error) => Description.Add(error);

    public static ApiResourceBasicsValidationErrors Validate(string name, string? displayName, string? description)
    {
        var errors = new ApiResourceBasicsValidationErrors();

        if (string.IsNullOrWhiteSpace(name))
            errors.AddNameError("Name is required.");
        else if (!ScopeValidationHelper.IsValidScopeName(name))
            errors.AddNameError(
                "Name contains invalid characters. Use alphanumeric characters, hyphens, dots, slashes, and colons.");
        else if (name.Length > ValidationConstants.MaxNameLength)
            errors.AddNameError($"Name cannot exceed {ValidationConstants.MaxNameLength} characters.");

        if (displayName is not null && displayName.Length > ValidationConstants.MaxDisplayNameLength)
            errors.AddDisplayNameError(
                $"Display Name cannot exceed {ValidationConstants.MaxDisplayNameLength} characters.");

        if (description is not null && description.Length > ValidationConstants.MaxDescriptionLength)
            errors.AddDescriptionError(
                $"Description cannot exceed {ValidationConstants.MaxDescriptionLength} characters.");

        return errors;
    }

    public void AddToModelState(ModelStateDictionary modelState, string prefix = "Basics")
    {
        foreach (string error in Name) modelState.AddModelError($"{prefix}.{nameof(Name)}", error);

        foreach (string error in DisplayName) modelState.AddModelError($"{prefix}.{nameof(DisplayName)}", error);

        foreach (string error in Description) modelState.AddModelError($"{prefix}.{nameof(Description)}", error);
    }

    public ValidationErrorDictionary ToDictionary()
    {
        var dict = new ValidationErrorDictionary();
        if (Name.Count > 0) dict.AddErrors("Basics.Name", Name);
        if (DisplayName.Count > 0) dict.AddErrors("Basics.DisplayName", DisplayName);
        if (Description.Count > 0) dict.AddErrors("Basics.Description", Description);
        return dict;
    }
}