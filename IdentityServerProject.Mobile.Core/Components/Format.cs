using System.Globalization;

namespace IdentityServerProject.Mobile.Core.Components;

/// <summary>Display formatting shared by the screens.</summary>
internal static class Format
{
    /// <summary>"Edited 14 Mar 2027 09:30", in the time zone the phone is in now.</summary>
    public static string Edited(DateTimeOffset at, TimeProvider time) =>
        "Edited " + TimeZoneInfo.ConvertTime(at, time.LocalTimeZone).ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture);

    /// <summary>The first line of <paramref name="text" />, cut to <paramref name="length" /> characters; empty when there is none.</summary>
    public static string Excerpt(string? text, int length = 80)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string line = text.Trim().Split('\n', 2)[0].Trim();
        return line.Length <= length ? line : line[..length].TrimEnd() + "…";
    }

    public static string Initial(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();

    public static string FirstName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "there" : name.Trim().Split(' ', 2)[0];
}
