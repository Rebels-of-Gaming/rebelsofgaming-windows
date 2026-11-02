using RoG.Core.Updates;

namespace RoG.Core.Tests.Updates;

public class StoreUpdateMonitorTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeStore(bool store = true) : IStoreUpdateSource
    {
        public bool IsStoreInstall { get; } = store;

        public StoreUpdateCheck Next { get; set; } = new(0, false);

        public Exception? Fail { get; set; }

        public StoreInstallResult InstallResult { get; set; } = StoreInstallResult.Completed;

        public int Checks { get; private set; }

        public Task<StoreUpdateCheck> CheckAsync(CancellationToken cancellationToken = default)
        {
            Checks++;
            return Fail is null ? Task.FromResult(Next) : Task.FromException<StoreUpdateCheck>(Fail);
        }

        public Task<StoreInstallResult> InstallAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            progress?.Report(1);
            return Task.FromResult(InstallResult);
        }
    }

    [Fact]
    public async Task Sideload_installs_never_ask_the_store()
    {
        var store = new FakeStore(store: false) { Next = new(1, true) };
        var monitor = new StoreUpdateMonitor(store);

        Assert.Equal(StoreUpdateBanner.None, await monitor.CheckAsync(force: true));
        Assert.False(monitor.IsDue);
        Assert.Equal(0, store.Checks);
    }

    [Fact]
    public async Task Checks_at_start_and_then_every_six_hours()
    {
        var time = new Clock(DateTimeOffset.Parse("2026-11-02T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var store = new FakeStore { Next = new(1, false) };
        var monitor = new StoreUpdateMonitor(store, time);

        Assert.Equal(StoreUpdateBanner.Available, await monitor.CheckAsync());
        await monitor.CheckAsync();
        Assert.Equal(1, store.Checks);

        time.Advance(TimeSpan.FromHours(6));
        await monitor.CheckAsync();
        Assert.Equal(2, store.Checks);
    }

    [Fact]
    public async Task Later_hides_an_optional_update_until_the_next_check_but_never_a_mandatory_one()
    {
        var store = new FakeStore { Next = new(1, false) };
        var monitor = new StoreUpdateMonitor(store);
        await monitor.CheckAsync();

        monitor.Later();
        Assert.Equal(StoreUpdateBanner.None, monitor.Banner);
        Assert.Equal(StoreUpdateBanner.Available, await monitor.CheckAsync(force: true));

        store.Next = new(2, true);
        Assert.Equal(StoreUpdateBanner.Mandatory, await monitor.CheckAsync(force: true));
        monitor.Later();
        Assert.Equal(StoreUpdateBanner.Mandatory, monitor.Banner);
    }

    [Fact]
    public async Task Store_errors_keep_the_app_running_and_change_nothing()
    {
        var store = new FakeStore { Fail = new InvalidOperationException("offline") };
        var monitor = new StoreUpdateMonitor(store);
        var changed = 0;
        monitor.Changed += (_, _) => changed++;

        Assert.Equal(StoreUpdateBanner.None, await monitor.CheckAsync());
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Failed_install_keeps_the_banner_and_completed_install_clears_it()
    {
        var store = new FakeStore { Next = new(1, false), InstallResult = StoreInstallResult.Failed };
        var monitor = new StoreUpdateMonitor(store);
        await monitor.CheckAsync();

        Assert.Equal(StoreInstallResult.Failed, await monitor.InstallAsync());
        Assert.Equal(StoreUpdateBanner.Available, monitor.Banner);

        store.InstallResult = StoreInstallResult.Completed;
        var progress = new List<double>();
        Assert.Equal(StoreInstallResult.Completed, await monitor.InstallAsync(new SyncProgress(progress)));
        Assert.Equal(StoreUpdateBanner.None, monitor.Banner);
        Assert.Equal([1d], progress);
    }

    private sealed class SyncProgress(List<double> values) : IProgress<double>
    {
        public void Report(double value) => values.Add(value);
    }
}
