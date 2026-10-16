using RoG.Core.Features.Servers;

namespace RoG.Core.Tests.Features.Servers;

public class ServerConnectorTests
{
    private sealed class FakeLauncher(params string[] installedSchemes) : IExternalLauncher
    {
        public List<Uri> Launched { get; } = [];

        public bool LaunchSucceeds { get; init; } = true;

        public Task<bool> CanLaunchAsync(Uri uri) => Task.FromResult(installedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase));

        public Task<bool> LaunchAsync(Uri uri)
        {
            Launched.Add(uri);
            return Task.FromResult(LaunchSucceeds);
        }
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; private set; }

        public bool Locked { get; init; }

        public void SetText(string text)
        {
            if (Locked)
            {
                throw new InvalidOperationException("Zwischenablage gesperrt");
            }

            Text = text;
        }
    }

    private static GameServer Server(string? connectUrl, ServerQueryType type = ServerQueryType.Source, string address = "cs.rogclan.de:27015", bool passworded = false) =>
        new("01H", "s", "RoG", null, null, address, type, connectUrl, true, null, 0, 0, passworded, null);

    [Theory]
    [InlineData("steam://connect/cs.rogclan.de:27015", ConnectKind.Steam)]
    [InlineData("et://et.rogclan.de:27960", ConnectKind.EnemyTerritory)]
    [InlineData("wolf://rtcw.rogclan.de:27960", ConnectKind.EnemyTerritory)]
    [InlineData("minecraft://?addExternalServer=RoG|mc.rogclan.de:19132", ConnectKind.Minecraft)]
    [InlineData("https://rogclan.de/server", ConnectKind.Web)]
    [InlineData("STEAM://connect/1.2.3.4:27015", ConnectKind.Steam)]
    public void Plan_uses_the_api_link(string url, ConnectKind kind)
    {
        var plan = ServerConnector.Plan(Server(url));
        Assert.Equal(kind, plan.Kind);
        Assert.Equal(new Uri(url), plan.LaunchUri);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("javascript:alert(1)")]
    [InlineData("steam://")]
    [InlineData("steam://connect/a b")]
    [InlineData("not a link")]
    [InlineData("")]
    public void Plan_never_launches_other_links(string url)
    {
        var plan = ServerConnector.Plan(Server(url));
        Assert.Null(plan.LaunchUri);
        Assert.Equal(ConnectKind.None, plan.Kind);
    }

    [Fact]
    public void Minecraft_without_link_is_a_manual_connect()
    {
        var plan = ServerConnector.Plan(Server(null, ServerQueryType.Minecraft, "mc.rogclan.de:25565"));
        Assert.Equal(ConnectKind.Minecraft, plan.Kind);
        Assert.Null(plan.LaunchUri);
        Assert.Equal("mc.rogclan.de:25565", plan.Address);
    }

    [Fact]
    public async Task Launches_steam_when_installed()
    {
        var launcher = new FakeLauncher("steam");
        var clipboard = new FakeClipboard();

        var outcome = await new ServerConnector(launcher, clipboard).ConnectAsync(Server("steam://connect/cs.rogclan.de:27015", passworded: true));

        Assert.Equal(ConnectResult.Launched, outcome.Result);
        Assert.Equal(ConnectKind.Steam, outcome.Kind);
        Assert.True(outcome.Passworded);
        Assert.Single(launcher.Launched);
        Assert.Null(clipboard.Text);
    }

    [Fact]
    public async Task Copies_the_address_when_the_game_is_missing()
    {
        var launcher = new FakeLauncher("steam");
        var clipboard = new FakeClipboard();

        var outcome = await new ServerConnector(launcher, clipboard).ConnectAsync(Server("et://et.rogclan.de:27960", ServerQueryType.Quake3, "et.rogclan.de:27960"));

        Assert.Equal(ConnectResult.CopiedNoHandler, outcome.Result);
        Assert.Equal(ConnectKind.EnemyTerritory, outcome.Kind);
        Assert.Empty(launcher.Launched);
        Assert.Equal("et.rogclan.de:27960", clipboard.Text);
    }

    [Fact]
    public async Task Copies_the_address_when_launching_fails()
    {
        var launcher = new FakeLauncher("steam") { LaunchSucceeds = false };
        var clipboard = new FakeClipboard();

        var outcome = await new ServerConnector(launcher, clipboard).ConnectAsync(Server("steam://connect/cs.rogclan.de:27015"));

        Assert.Equal(ConnectResult.CopiedNoHandler, outcome.Result);
        Assert.Equal("cs.rogclan.de:27015", clipboard.Text);
    }

    [Fact]
    public async Task Minecraft_copies_the_address()
    {
        var clipboard = new FakeClipboard();

        var outcome = await new ServerConnector(new FakeLauncher("minecraft"), clipboard).ConnectAsync(Server(null, ServerQueryType.Minecraft, "mc.rogclan.de:25565"));

        Assert.Equal(ConnectResult.CopiedManual, outcome.Result);
        Assert.Equal(ConnectKind.Minecraft, outcome.Kind);
        Assert.Equal("mc.rogclan.de:25565", clipboard.Text);
    }

    [Fact]
    public async Task Web_links_open_without_scheme_check()
    {
        var launcher = new FakeLauncher();

        var outcome = await new ServerConnector(launcher, new FakeClipboard()).ConnectAsync(Server("https://rogclan.de/server"));

        Assert.Equal(ConnectResult.Launched, outcome.Result);
        Assert.Single(launcher.Launched);
    }

    [Fact]
    public async Task Reports_failure_when_the_clipboard_is_locked()
    {
        var outcome = await new ServerConnector(new FakeLauncher(), new FakeClipboard { Locked = true }).ConnectAsync(Server(null, ServerQueryType.Minecraft));

        Assert.Equal(ConnectResult.Failed, outcome.Result);
    }
}

public class ConnectMessageTests
{
    private sealed class KeyLocalizer : RoG.Core.Localization.ILocalizer
    {
        public string GetString(string key) => key + "|{0}";
    }

    [Theory]
    [InlineData(ConnectResult.Launched, ConnectKind.Steam, "Servers_LaunchedSteam|RoG CS", MessageTone.Success)]
    [InlineData(ConnectResult.Launched, ConnectKind.EnemyTerritory, "Servers_LaunchedGame|RoG CS", MessageTone.Success)]
    [InlineData(ConnectResult.CopiedNoHandler, ConnectKind.EnemyTerritory, "Servers_NoHandlerEt|1.2.3.4:27960", MessageTone.Info)]
    [InlineData(ConnectResult.CopiedNoHandler, ConnectKind.Steam, "Servers_NoHandlerSteam|1.2.3.4:27960", MessageTone.Info)]
    [InlineData(ConnectResult.CopiedManual, ConnectKind.Minecraft, "Servers_ManualMinecraft|1.2.3.4:27960", MessageTone.Info)]
    [InlineData(ConnectResult.Failed, ConnectKind.None, "Servers_CopyFailed|1.2.3.4:27960", MessageTone.Error)]
    public void Describes_each_outcome(ConnectResult result, ConnectKind kind, string text, MessageTone tone)
    {
        var message = ConnectMessage.Describe(new ConnectOutcome(result, kind, "1.2.3.4:27960", false), "RoG CS", new KeyLocalizer());
        Assert.Equal(text, message.Text);
        Assert.Equal(tone, message.Tone);
    }

    [Fact]
    public void Adds_the_password_hint()
    {
        var message = ConnectMessage.Describe(new ConnectOutcome(ConnectResult.Launched, ConnectKind.Steam, "a:1", true), "X", new KeyLocalizer());
        Assert.EndsWith("Servers_Passworded|{0}", message.Text, StringComparison.Ordinal);
    }
}
