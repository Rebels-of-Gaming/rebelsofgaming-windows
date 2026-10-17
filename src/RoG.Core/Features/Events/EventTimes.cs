using System.Globalization;

namespace RoG.Core.Features.Events;

/// <summary>Zeitangaben von Terminen – Datum und Uhrzeit getrennt formatiert (ein „t“ in einem eigenen Muster wäre das AM/PM-Kürzel).</summary>
public static class EventTimes
{
    /// <summary>„Fr., 16. Oktober 2026 · 20:30–21:00“; endet der Termin an einem anderen Tag, mit Datum.</summary>
    public static string Range(DateTimeOffset start, DateTimeOffset? end, CultureInfo culture, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(culture);
        zone ??= TimeZoneInfo.Local;
        var s = TimeZoneInfo.ConvertTime(start, zone).DateTime;
        var text = s.ToString("ddd, d. MMMM yyyy", culture) + " · " + s.ToString("t", culture);
        if (end is { } endsAt)
        {
            var e = TimeZoneInfo.ConvertTime(endsAt, zone).DateTime;
            text += "–" + (e.Date == s.Date ? e.ToString("t", culture) : e.ToString("g", culture));
        }

        return text;
    }
}
