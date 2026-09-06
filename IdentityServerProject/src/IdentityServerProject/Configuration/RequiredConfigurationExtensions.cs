namespace IdentityServerProject.Configuration;

/// <summary>
///     Reads settings that have no safe default. Seed credentials are the case this exists
///     for: a fallback password is inherited by every deployment that never sets one, works
///     perfectly, and is the same everywhere — so the value has to be an input, and its
///     absence has to stop the host rather than be quietly filled in.
/// </summary>
public static class RequiredConfigurationExtensions
{
    public static string Required(this IConfiguration configuration, string key)
    {
        string? value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Configuration '{key}' is required and has no default. " +
                "Supply it through the AppHost, user secrets, or the environment.");

        return value;
    }
}
