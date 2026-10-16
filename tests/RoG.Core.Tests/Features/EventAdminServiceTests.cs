using System.Text.Json;
using RoG.Core.Features.Events;

namespace RoG.Core.Tests.Features;

public class EventAdminServiceTests
{
    [Fact]
    public void Body_uses_local_time_with_timezone_and_weekly_series()
    {
        var body = EventAdminService.Body(new EventDraft(
            "training", " ET-Training ", new DateTime(2026, 10, 20, 20, 0, 0), 90,
            SquadId: "s1", LocationType: "teamspeak", Location: "RoG ET → Training",
            WeeklyDays: [4, 2, 2], Count: 8));
        var json = JsonSerializer.Serialize(body);

        Assert.Contains("\"starts_at\":\"2026-10-20 20:00\"", json, StringComparison.Ordinal);
        Assert.Contains("\"timezone\":\"Europe/Luxembourg\"", json, StringComparison.Ordinal);
        Assert.Contains("\"title\":{\"de\":\"ET-Training\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"weekdays\":[2,4]", json, StringComparison.Ordinal);
        Assert.Contains("\"ends\":\"count\"", json, StringComparison.Ordinal);
        Assert.Contains("\"squad_id\":\"s1\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Single_event_has_no_recurrence_and_clamps_duration()
    {
        var body = EventAdminService.Body(new EventDraft("clanabend", "Clanabend", new DateTime(2026, 10, 24, 20, 0, 0), 5));

        Assert.False(body.ContainsKey("recurrence"));
        Assert.Equal(15, body["duration_minutes"]);
        Assert.Throws<ArgumentException>(() => EventAdminService.Body(new EventDraft("x", " ", DateTime.Now, 60)));
    }
}
