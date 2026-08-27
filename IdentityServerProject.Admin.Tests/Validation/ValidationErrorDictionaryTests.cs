using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Admin.Tests.Validation;

public sealed class ValidationErrorDictionaryTests
{
    [Fact]
    public void Empty_ReturnsEmptyInstance()
    {
        var errors = ValidationErrorDictionary.Empty;

        Assert.True(errors.IsEmpty);
        Assert.False(errors.HasErrors);
        Assert.Empty(errors);
    }

    [Fact]
    public void AddError_SingleMessage_AddsMessageUnderKey()
    {
        var errors = new ValidationErrorDictionary();

        errors.AddError("ClientId", "Client ID is required.");

        Assert.True(errors.HasErrors);
        Assert.False(errors.IsEmpty);
        Assert.Single(errors);
        Assert.True(errors.ContainsKey("ClientId"));
        Assert.Equal(new[] { "Client ID is required." }, errors["ClientId"]);
    }

    [Fact]
    public void AddError_MultipleMessagesSameKey_AppendsMessages()
    {
        var errors = new ValidationErrorDictionary();

        errors.AddError("Password", "Password is too short.")
              .AddError("Password", "Password requires a special character.");

        Assert.Single(errors);
        Assert.Equal(new[] { "Password is too short.", "Password requires a special character." }, errors["Password"]);
    }

    [Fact]
    public void AddErrors_CollectionOfMessages_AddsAllMessages()
    {
        var errors = new ValidationErrorDictionary();

        errors.AddErrors("Email", new[] { "Email is required.", "Email is invalid." });

        Assert.Single(errors);
        Assert.Equal(2, errors["Email"].Length);
    }

    [Fact]
    public void AddError_NullField_TreatsAsEmptyStringKey()
    {
        var errors = new ValidationErrorDictionary();

        errors.AddError(null!, "General validation error.");

        Assert.True(errors.ContainsKey(string.Empty));
        Assert.Equal(new[] { "General validation error." }, errors[string.Empty]);
    }

    [Fact]
    public void TryGetValue_ExistingKey_ReturnsTrueAndArray()
    {
        var errors = new ValidationErrorDictionary();
        errors.AddError("Scope", "Invalid scope");

        var found = errors.TryGetValue("Scope", out var messages);

        Assert.True(found);
        Assert.Equal(new[] { "Invalid scope" }, messages);
    }

    [Fact]
    public void TryGetValue_MissingKey_ReturnsFalseAndEmptyArray()
    {
        var errors = new ValidationErrorDictionary();

        var found = errors.TryGetValue("Missing", out var messages);

        Assert.False(found);
        Assert.Empty(messages);
    }

    [Fact]
    public void ToDictionary_ReturnsReadOnlyDictionaryCopy()
    {
        var errors = new ValidationErrorDictionary();
        errors.AddError("Field1", "Error 1");
        errors.AddError("Field2", "Error 2");

        var dict = errors.ToDictionary();

        Assert.Equal(2, dict.Count);
        Assert.Equal(new[] { "Error 1" }, dict["Field1"]);
        Assert.Equal(new[] { "Error 2" }, dict["Field2"]);
    }

    [Fact]
    public void AdminMutationResult_ValidationFailure_WorksWithValidationErrorDictionary()
    {
        var errors = new ValidationErrorDictionary();
        errors.AddError("Input.Name", "Name is invalid.");

        var result = AdminMutationResult.ValidationFailure(errors);

        Assert.False(result.Succeeded);
        Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
        Assert.Equal("Name is invalid.", result.ErrorMessage);
        Assert.True(result.Errors.ContainsKey("Input.Name"));
    }
}
