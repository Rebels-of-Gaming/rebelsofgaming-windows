using RoG.Core.Security;

namespace RoG.Core.Tests.Security;

public class AppLockTests
{
    private DateTimeOffset _now = new(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);

    private AppLock Create(bool enabled) => new(() => _now) { Enabled = enabled, IdleTimeout = TimeSpan.FromMinutes(5) };

    [Fact]
    public void Locks_on_start_only_when_enabled()
    {
        Assert.True(Create(enabled: true).OnStart());
        Assert.False(Create(enabled: false).OnStart());
    }

    [Fact]
    public void Locks_after_idle_timeout_but_not_before()
    {
        var appLock = Create(enabled: true);
        appLock.OnStart();
        appLock.Unlock();

        appLock.OnLeft();
        _now += TimeSpan.FromMinutes(4);
        Assert.False(appLock.OnReturned());

        appLock.OnLeft();
        _now += TimeSpan.FromMinutes(5);
        Assert.True(appLock.OnReturned());
        Assert.True(appLock.IsLocked);

        appLock.Unlock();
        Assert.False(appLock.IsLocked);
    }

    [Fact]
    public void First_departure_counts_and_disabled_never_locks()
    {
        var appLock = Create(enabled: true);
        appLock.OnLeft();
        _now += TimeSpan.FromMinutes(3);
        appLock.OnLeft();
        _now += TimeSpan.FromMinutes(3);
        Assert.True(appLock.OnReturned());

        var off = Create(enabled: false);
        off.OnLeft();
        _now += TimeSpan.FromHours(1);
        Assert.False(off.OnReturned());
    }
}
