namespace RoG.Core.Features.Events;

/// <summary>Tag im Monatsraster mit seinen Terminen (Ortszeit).</summary>
public sealed record MonthDay(DateOnly Date, bool InMonth, bool IsToday, IReadOnlyList<EventItem> Events);

/// <summary>
/// Monatsansicht des Kalenders: 6 Wochen ab Montag (wie im deutschsprachigen Raum üblich), Termine nach
/// lokalem Starttag sortiert. Rein rechnerisch, damit testbar.
/// </summary>
public static class MonthGrid
{
    public static DateOnly FirstCell(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7; // Montag = 0
        return first.AddDays(-offset);
    }

    public static IReadOnlyList<MonthDay> Build(int year, int month, IEnumerable<EventItem> events, DateOnly today, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        var tz = zone ?? TimeZoneInfo.Local;
        var byDay = events
            .Where(e => e.StartsAt is not null)
            .GroupBy(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.StartsAt!.Value, tz).DateTime))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EventItem>)g.OrderBy(e => e.StartsAt).ToList());
        var start = FirstCell(year, month);
        return Enumerable.Range(0, 42)
            .Select(i => start.AddDays(i))
            .Select(d => new MonthDay(d, d.Month == month, d == today, byDay.TryGetValue(d, out var list) ? list : []))
            .ToList();
    }
}
