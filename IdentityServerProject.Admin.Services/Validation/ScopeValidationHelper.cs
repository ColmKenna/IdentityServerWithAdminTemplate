using System.Text.RegularExpressions;

namespace IdentityServerProject.Services.Validation;

public static class ScopeValidationHelper
{
    // Allows alphanumeric, hyphen, dot, slash, and colon. Prevents spaces and most special characters.
    private static readonly Regex ValidScopeNameRegex = new Regex(@"^[\w\-\.\/:]+$", RegexOptions.Compiled);

    public static bool IsValidScopeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return ValidScopeNameRegex.IsMatch(name);
    }
}
