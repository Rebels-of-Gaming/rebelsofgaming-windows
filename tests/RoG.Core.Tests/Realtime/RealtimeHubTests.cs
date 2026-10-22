using RoG.Core.Features.Notifications;
using RoG.Core.Realtime;

namespace RoG.Core.Tests.Realtime;

public class RealtimeHubTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);

    private static RealtimeHub Hub() => new(_ => Task.FromResult<RealtimeConfig?>(null), _ => throw new InvalidOperationException(), () => s_now);

    [Fact]
    public void Laravel_notification_event_becomes_a_notification()
    {
        var hub = Hub();
        NotificationItem? received = null;
        hub.NotificationReceived += (_, n) => received = n;

        hub.Dispatch(new ReverbEvent("private-App.Models.User.01U", RealtimeHub.NotificationEvent,
            """{"id":"9b1d","type":"forum.mention","title":"Neue Erwähnung","body":"Wahke hat dich erwähnt","url":"https://rebelsofgaming.com/forum/x","icon":"at-sign"}"""));

        Assert.NotNull(received);
        Assert.Equal("9b1d", received.Id);
        Assert.Equal("forum.mention", received.Type);
        Assert.Equal("https://rebelsofgaming.com/forum/x", received.Url!.ToString());
        Assert.True(received.IsUnread);
        Assert.Equal(s_now, received.CreatedAt);
    }

    [Fact]
    public void Counters_and_live_updates()
    {
        var hub = Hub();
        int? unread = null;
        int? messages = null;
        var live = 0;
        var friends = 0;
        hub.FriendsChanged += (_, _) => friends++;
        hub.UnreadChanged += (_, n) => unread = n;
        hub.MessagesUnreadChanged += (_, n) => messages = n;
        hub.LiveChanged += (_, _) => live++;

        hub.Dispatch(new ReverbEvent("private-App.Models.User.01U", ".inbox.changed", """{"unread":5}"""));
        hub.Dispatch(new ReverbEvent("private-App.Models.User.01U", ".messages.unread", """{"unread":2}"""));
        hub.Dispatch(new ReverbEvent("live", ".live.updated", """{"at":"2026-10-16T20:00:00Z"}"""));

        Assert.Equal(5, unread);
        Assert.Equal(2, messages);
        Assert.Equal(1, live);
        Assert.Equal(0, friends);

        hub.Dispatch(new ReverbEvent("private-App.Models.User.01U", ".friends.changed", """{"at":"2026-10-22T18:04:11.123Z"}"""));
        Assert.Equal(1, friends);
    }

    [Fact]
    public void Server_status_update()
    {
        var hub = Hub();
        ServerStatusUpdate? status = null;
        hub.ServerStatusChanged += (_, s) => status = s;

        hub.Dispatch(new ReverbEvent("servers", ".server.status", """{"id":"01S","slug":"et","online":true,"map":"supply","players":12,"max_players":24}"""));

        Assert.Equal(new ServerStatusUpdate("01S", "et", true, "supply", 12, 24), status);
    }

    [Fact]
    public void Broken_payloads_are_ignored()
    {
        var hub = Hub();
        var calls = 0;
        hub.NotificationReceived += (_, _) => calls++;
        hub.UnreadChanged += (_, _) => calls++;

        hub.Dispatch(new ReverbEvent("x", RealtimeHub.NotificationEvent, "kein json"));
        hub.Dispatch(new ReverbEvent("x", RealtimeHub.NotificationEvent, """{"title":"ohne id"}"""));
        hub.Dispatch(new ReverbEvent("x", ".inbox.changed", """{"unread":"viele"}"""));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Without_realtime_config_the_hub_stays_off()
    {
        var hub = Hub();
        Assert.False(await hub.StartAsync("01U"));
        Assert.Equal(RealtimeState.Disconnected, hub.State);
    }
}
