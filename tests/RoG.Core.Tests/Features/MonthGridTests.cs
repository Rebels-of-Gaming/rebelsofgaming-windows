using RoG.Core.Features.Events;

namespace RoG.Core.Tests.Features;

public class MonthGridTests
{
    private static EventItem Event(string id, DateTimeOffset start) =>
        new(id, id, "Training", null, start, null, null, false, false, null, null, 0, 0, null, Rsvp.None, null, null, null);

    [Fact]
    public void October_2026_starts_on_monday_28_september()
    {
        Assert.Equal(new DateOnly(2026, 9, 28), MonthGrid.FirstCell(2026, 10));
        Assert.Equal(new DateOnly(2026, 6, 1), MonthGrid.FirstCell(2026, 6)); // 1. Juni 2026 ist ein Montag
    }

    [Fact]
    public void Events_land_on_their_local_day()
    {
        var utc = TimeZoneInfo.Utc;
        var grid = MonthGrid.Build(2026, 10, [Event("a", new DateTimeOffset(2026, 10, 16, 18, 0, 0, TimeSpan.Zero)), Event("b", new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero))], new DateOnly(2026, 10, 16), utc);

        Assert.Equal(42, grid.Count);
        var day = grid.Single(d => d.Date == new DateOnly(2026, 10, 16));
        Assert.True(day.IsToday);
        Assert.Equal(["b", "a"], day.Events.Select(e => e.Id));
        Assert.False(grid[0].InMonth);
    }
}
