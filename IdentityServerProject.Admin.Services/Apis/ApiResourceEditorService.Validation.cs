using System;
using System.Collections.Generic;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.Apis;

public partial class ApiResourceEditorService
{
    private static string? NormalizeNullableString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddIdentifierError(
        ValidationErrorDictionary errors,
        string field,
        string displayName,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.AddError(field, $"{displayName} name is required.");
        }
        else if (value.Length > ValidationConstants.MaxNameLength)
        {
            errors.AddError(field, $"{displayName} name cannot exceed {ValidationConstants.MaxNameLength} characters.");
        }
        else if (!ScopeValidationHelper.IsValidScopeName(value))
        {
            errors.AddError(field, $"{displayName} name contains invalid characters.");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        var isKnownConstraint = message.Contains("IX_ApiResources_Name", StringComparison.OrdinalIgnoreCase)
            || message.Contains("IX_ApiScopes_Name", StringComparison.OrdinalIgnoreCase)
            || message.Contains("IX_ApiResourceScopes_ApiResourceId_Scope", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ApiResources.Name", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ApiScopes.Name", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ApiResourceScopes.ApiResourceId, ApiResourceScopes.Scope", StringComparison.OrdinalIgnoreCase);

        return isKnownConstraint && UniqueConstraintViolationDetector.IsUniqueConstraintViolation(exception);
    }
}
