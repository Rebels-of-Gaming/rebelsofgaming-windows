using System.Globalization;
using RoG.Core.Features.Events;

namespace RoG.Core.Tests.Features;

public class EventTimesTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    [Fact]
    public void Same_day_shows_start_and_end_time_without_am_pm_letter()
    {
        var start = new DateTimeOffset(2026, 10, 16, 20, 30, 0, TimeSpan.Zero);
        var text = EventTimes.Range(start, start.AddMinutes(30), De, TimeZoneInfo.Utc);

        Assert.Equal("Fr, 16. Oktober 2026 · 20:30–21:00", text.Replace(".,", ",", StringComparison.Ordinal));
        Assert.DoesNotContain("P", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Other_day_end_includes_date()
    {
        var start = new DateTimeOffset(2026, 10, 16, 22, 0, 0, TimeSpan.Zero);
        var text = EventTimes.Range(start, start.AddHours(4), De, TimeZoneInfo.Utc);

        Assert.Contains("17.10.2026 02:00", text, StringComparison.Ordinal);
    }

    [Fact]
    public void English_uses_culture_time_format()
    {
        var start = new DateTimeOffset(2026, 10, 16, 20, 30, 0, TimeSpan.Zero);
        var text = EventTimes.Range(start, null, CultureInfo.GetCultureInfo("en-US"), TimeZoneInfo.Utc);

        Assert.Contains("8:30", text, StringComparison.Ordinal);
        Assert.EndsWith("PM", text, StringComparison.Ordinal); // ICU setzt ein schmales Leerzeichen davor
    }
}
