using System.Text.RegularExpressions;

namespace RoG.Core.Content;

/// <summary>Texte aus Spielen (Clan-Tags, Spielernamen, Hostnamen).</summary>
public static partial class GameText
{
    /// <summary>
    /// Quake-/ET-Farbcodes (<c>^1</c>, <c>^a</c> …) entfernen – wie die Website (<c>Quake3::strip</c>). Farbig darstellen
    /// geht nicht sinnvoll: <c>^0</c> wäre schwarz auf dunklem Hintergrund.
    /// </summary>
    public static string? StripColors(string? text) =>
        text is null ? null : ColorCode().Replace(text, string.Empty).Trim() is { Length: > 0 } clean ? clean : null;

    [GeneratedRegex(@"\^.")]
    private static partial Regex ColorCode();
}
