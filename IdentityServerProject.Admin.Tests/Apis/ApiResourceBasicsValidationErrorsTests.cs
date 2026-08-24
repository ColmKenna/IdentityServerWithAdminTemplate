using IdentityServerProject.Services.Apis;
using IdentityServerProject.Services.Validation;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Apis;

public class ApiResourceBasicsValidationErrorsTests
{
    [Fact]
    public void Validate_ValidInputs_HasNoErrorsAndIsValid()
    {
        var errors = ApiResourceBasicsValidationErrors.Validate("api-resource-1", "Display Name", "Description");

        Assert.True(errors.IsValid);
        Assert.False(errors.HasErrors);
        Assert.Empty(errors.Name);
        Assert.Empty(errors.DisplayName);
        Assert.Empty(errors.Description);
        Assert.Empty(errors.ToDictionary());
    }

    [Fact]
    public void Validate_EmptyOrWhitespaceName_AddsNameRequiredError()
    {
        var errors = ApiResourceBasicsValidationErrors.Validate("   ", null, null);

        Assert.False(errors.IsValid);
        Assert.True(errors.HasErrors);
        Assert.Single(errors.Name);
        Assert.Equal("Name is required.", errors.Name[0]);
        var dict = errors.ToDictionary();
        Assert.True(dict.ContainsKey("Basics.Name"));
        Assert.Equal("Name is required.", dict["Basics.Name"][0]);
    }

    [Fact]
    public void Validate_InvalidScopeCharactersInName_AddsInvalidCharactersError()
    {
        var errors = ApiResourceBasicsValidationErrors.Validate("invalid name with spaces", null, null);

        Assert.False(errors.IsValid);
        Assert.True(errors.HasErrors);
        Assert.Single(errors.Name);
        Assert.Contains("invalid characters", errors.Name[0]);
    }

    [Fact]
    public void Validate_OverlongName_AddsLengthError()
    {
        var overlongName = new string('a', ValidationConstants.MaxNameLength + 1);
        var errors = ApiResourceBasicsValidationErrors.Validate(overlongName, null, null);

        Assert.False(errors.IsValid);
        Assert.True(errors.HasErrors);
        Assert.Single(errors.Name);
        Assert.Contains($"cannot exceed {ValidationConstants.MaxNameLength} characters", errors.Name[0]);
    }

    [Fact]
    public void Validate_OverlongDisplayName_AddsDisplayNameError()
    {
        var overlongDisplayName = new string('b', ValidationConstants.MaxDisplayNameLength + 1);
        var errors = ApiResourceBasicsValidationErrors.Validate("valid-name", overlongDisplayName, null);

        Assert.False(errors.IsValid);
        Assert.True(errors.HasErrors);
        Assert.Empty(errors.Name);
        Assert.Single(errors.DisplayName);
        Assert.Contains($"cannot exceed {ValidationConstants.MaxDisplayNameLength} characters", errors.DisplayName[0]);
        var dict = errors.ToDictionary();
        Assert.True(dict.ContainsKey("Basics.DisplayName"));
    }

    [Fact]
    public void Validate_OverlongDescription_AddsDescriptionError()
    {
        var overlongDescription = new string('c', ValidationConstants.MaxDescriptionLength + 1);
        var errors = ApiResourceBasicsValidationErrors.Validate("valid-name", "valid-display", overlongDescription);

        Assert.False(errors.IsValid);
        Assert.True(errors.HasErrors);
        Assert.Empty(errors.Name);
        Assert.Empty(errors.DisplayName);
        Assert.Single(errors.Description);
        Assert.Contains($"cannot exceed {ValidationConstants.MaxDescriptionLength} characters", errors.Description[0]);
        var dict = errors.ToDictionary();
        Assert.True(dict.ContainsKey("Basics.Description"));
    }

    [Fact]
    public void Validate_MultipleErrors_PopulatesAllFieldsAndDictionaryKeys()
    {
        var overlongName = new string('a', ValidationConstants.MaxNameLength + 1);
        var overlongDisplayName = new string('b', ValidationConstants.MaxDisplayNameLength + 1);
        var overlongDescription = new string('c', ValidationConstants.MaxDescriptionLength + 1);

        var errors = ApiResourceBasicsValidationErrors.Validate(overlongName, overlongDisplayName, overlongDescription);

        Assert.False(errors.IsValid);
        Assert.True(errors.HasErrors);
        Assert.NotEmpty(errors.Name);
        Assert.NotEmpty(errors.DisplayName);
        Assert.NotEmpty(errors.Description);

        var dict = errors.ToDictionary();
        Assert.True(dict.ContainsKey("Basics.Name"));
        Assert.True(dict.ContainsKey("Basics.DisplayName"));
        Assert.True(dict.ContainsKey("Basics.Description"));
    }

    [Fact]
    public void AddMethods_DirectlyPopulateErrorLists()
    {
        var container = new ApiResourceBasicsValidationErrors();
        Assert.True(container.IsValid);

        container.AddNameError("Custom name error");
        container.AddDisplayNameError("Custom display name error");
        container.AddDescriptionError("Custom description error");

        Assert.False(container.IsValid);
        Assert.True(container.HasErrors);
        Assert.Contains("Custom name error", container.Name);
        Assert.Contains("Custom display name error", container.DisplayName);
        Assert.Contains("Custom description error", container.Description);
    }
}
