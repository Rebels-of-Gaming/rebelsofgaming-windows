namespace RoG.Core.Layout;

/// <summary>
/// Spaltenrechnung für Kachel-Raster mit unterschiedlich hohen Karten (Server-Karten): so viele Spalten wie mit
/// Mindestbreite und Abstand passen, Breite gleichmäßig verteilt. Jede Zeile ist so hoch wie ihre höchste Karte –
/// anders als <c>UniformGridLayout</c>, das alle Karten auf die Höhe der ersten zwingt und den Rest abschneidet.
/// </summary>
public static class CardGrid
{
    /// <summary>Spaltenzahl (mindestens 1, höchstens <paramref name="count"/>, wenn &gt; 0).</summary>
    public static int Columns(double width, double minItemWidth, double spacing, int count = 0)
    {
        var columns = double.IsFinite(width) && width > 0 && minItemWidth > 0
            ? (int)Math.Floor((width + spacing) / (minItemWidth + spacing))
            : 1;
        columns = Math.Max(1, columns);
        return count > 0 ? Math.Min(columns, count) : columns;
    }

    /// <summary>Breite je Karte bei <paramref name="columns"/> Spalten.</summary>
    public static double ItemWidth(double width, int columns, double spacing) =>
        Math.Max(0, (width - ((columns - 1) * spacing)) / columns);

    /// <summary>Gesamthöhe aus den Zeilenhöhen samt Abständen.</summary>
    public static double TotalHeight(IReadOnlyList<double> rowHeights, double spacing) =>
        rowHeights.Count == 0 ? 0 : rowHeights.Sum() + ((rowHeights.Count - 1) * spacing);
}
