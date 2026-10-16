namespace RoG.Core.Content;

/// <summary>Ergebnis einer Bearbeitung im Textfeld: neuer Text und neue Markierung.</summary>
public sealed record TextEdit(string Text, int SelectionStart, int SelectionLength);

/// <summary>BBCode-Leiste für Forum-Antworten: Tags um die Markierung setzen (ohne Markierung: Platzhalter markiert).</summary>
public static class BbCode
{
    /// <summary>
    /// <c>[tag]markierung[/tag]</c> bzw. <c>[tag=wert]…</c>. Ohne Markierung wird zwischen die Tags gesprungen,
    /// damit man direkt weitertippen kann.
    /// </summary>
    public static TextEdit Wrap(string text, int selectionStart, int selectionLength, string tag, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        var start = Math.Clamp(selectionStart, 0, text.Length);
        var length = Math.Clamp(selectionLength, 0, text.Length - start);
        var open = value is null ? $"[{tag}]" : $"[{tag}={value}]";
        var close = $"[/{tag}]";
        var inner = text.Substring(start, length);
        var result = text[..start] + open + inner + close + text[(start + length)..];
        return new TextEdit(result, start + open.Length, inner.Length);
    }

    /// <summary>Zitat eines Beitrags für die Antwort: <c>[quote=Name]Text[/quote]</c> + Leerzeile.</summary>
    public static string Quote(string author, string plainText)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(plainText);
        var name = author.Replace("]", string.Empty, StringComparison.Ordinal).Replace("[", string.Empty, StringComparison.Ordinal);
        return $"[quote={name}]{plainText.Trim()}[/quote]\n\n";
    }
}
