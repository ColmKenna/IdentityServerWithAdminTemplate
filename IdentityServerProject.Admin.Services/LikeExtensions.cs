namespace IdentityServerProject.Services;

public static class LikeExtensions
{
    /// <summary>
    /// Escapes characters used as wildcards in SQL LIKE statements (%, _, [, ])
    /// so they can be searched as literal characters.
    /// </summary>
    /// <param name="input">The string to escape.</param>
    /// <returns>The escaped string.</returns>
    public static string? EscapeLikePattern(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var escaped = new System.Text.StringBuilder(input.Length);
        foreach (var character in input)
        {
            escaped.Append(character switch
            {
                '[' => "[[]",
                ']' => "[]]",
                '%' => "[%]",
                '_' => "[_]",
                _ => character.ToString()
            });
        }

        return escaped.ToString();
    }
}
