using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Features.Tools;

namespace RoG.Core.Tests.Features.Tools;

public class ToolBridgeTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeApi : IToolBridgeApi
    {
        public List<string> Calls { get; } = [];

        public JsonNode? Stored { get; set; }

        public Func<Exception?> Fail { get; set; } = () => null;

        public TaskCompletionSource? Gate { get; set; }

        public IReadOnlyList<ToolAsset> Assets { get; set; } = [new("heroes/arthur.png", "image/png", 3, 64, 64, "h1")];

        private async Task Enter(string call)
        {
            Calls.Add(call);
            if (Gate is { } gate)
            {
                await gate.Task;
            }

            if (Fail() is { } ex)
            {
                throw ex;
            }
        }

        public async Task<JsonNode?> ShowAsync(string slug, CancellationToken cancellationToken)
        {
            await Enter($"show {slug}");
            return JsonNode.Parse("""{"id":"t1","slug":"et","viewer":{"id":"01U","name":"Wahke","is_member":true,"can_manage":true,"can_write_squad":true,"squad_role":null}}""");
        }

        public async Task<JsonNode?> SquadAsync(string slug, CancellationToken cancellationToken)
        {
            await Enter("squad");
            return null;
        }

        public async Task<JsonNode?> DataGetAsync(string slug, string scope, string key, CancellationToken cancellationToken)
        {
            await Enter($"get {scope} {key}");
            return Stored is null ? null : new JsonObject { ["key"] = key, ["value"] = Stored.DeepClone() };
        }

        public async Task<JsonNode?> DataSetAsync(string slug, string scope, string key, JsonNode? value, CancellationToken cancellationToken)
        {
            await Enter($"set {scope} {key}");
            Stored = value?.DeepClone();
            return new JsonObject { ["key"] = key, ["value"] = value?.DeepClone(), ["updated_at"] = "2026-10-19T12:00:00Z" };
        }

        public async Task DataDeleteAsync(string slug, string scope, string key, CancellationToken cancellationToken) => await Enter($"delete {scope} {key}");

        public async Task<ToolDataPage> DataListAsync(string slug, string scope, string? prefix, CancellationToken cancellationToken)
        {
            await Enter($"list {scope} {prefix}");
            return new ToolDataPage(JsonNode.Parse("""[{"key":"a","value":1}]"""), true);
        }

        public async Task<ToolDataPage> MembersAsync(string slug, string key, CancellationToken cancellationToken)
        {
            await Enter($"members {key}");
            return new ToolDataPage(new JsonArray(), false);
        }

        public async Task<IReadOnlyList<ToolAsset>> AssetsAsync(string slug, CancellationToken cancellationToken)
        {
            await Enter("assets");
            return Assets;
        }

        public async Task<ToolAssetContent> AssetAsync(string slug, string name, long maxBytes, CancellationToken cancellationToken)
        {
            await Enter($"asset {name}");
            return new ToolAssetContent("image/png", [1, 2, 3]);
        }
    }

    private sealed class FakeHost : IToolBridgeHost
    {
        public List<string> Copied { get; } = [];

        public List<Uri> Opened { get; } = [];

        public bool Allow { get; set; } = true;

        public int? Height { get; private set; }

        public Task CopyAsync(string text)
        {
            Copied.Add(text);
            return Task.CompletedTask;
        }

        public Task<bool> OpenLinkAsync(Uri url)
        {
            if (Allow)
            {
                Opened.Add(url);
            }

            return Task.FromResult(Allow);
        }

        public void Resize(int height) => Height = height;
    }

    private static (ToolBridge Bridge, FakeApi Api, FakeHost Host, Clock Clock) Create()
    {
        var api = new FakeApi();
        var host = new FakeHost();
        var clock = new Clock();
        return (new ToolBridge(api, host, "et-nickname", clock), api, host, clock);
    }

    private static string Request(string id, string method, string? parameters = null) =>
        $$"""{"rog":"bridge","v":1,"id":"{{id}}","method":"{{method}}","params":{{parameters ?? "{}"}}}""";

    private static JsonObject Parse(string? json) => Assert.IsType<JsonObject>(JsonNode.Parse(json!));

    private static string? ErrorCode(string? json) => (string?)Parse(json)["error"]?["code"];

    [Fact]
    public async Task Ignores_foreign_messages_and_answers_ready_with_theme()
    {
        var (bridge, api, _, _) = Create();

        Assert.Null(await bridge.HandleAsync("""{"rog":"other","v":1,"id":"1","method":"me"}"""));
        Assert.Null(await bridge.HandleAsync("""{"rog":"bridge","v":2,"id":"1","method":"me"}"""));
        Assert.Null(await bridge.HandleAsync("kein json"));
        Assert.Empty(api.Calls);

        var theme = Parse(await bridge.HandleAsync("""{"rog":"bridge","v":1,"method":"ready","params":{"version":"1"}}"""));
        Assert.Equal("theme", (string?)theme["type"]);
        Assert.Equal("dark", (string?)theme["theme"]);
    }

    [Fact]
    public async Task Me_returns_the_viewer()
    {
        var (bridge, _, _, _) = Create();

        var response = Parse(await bridge.HandleAsync(Request("r1", "me")));

        Assert.Equal("r1", (string?)response["id"]);
        Assert.True((bool)response["ok"]!);
        Assert.Equal("Wahke", (string?)response["data"]!["name"]);
    }

    [Fact]
    public async Task Data_roundtrip_keeps_objects_and_defaults_to_personal()
    {
        var (bridge, api, _, _) = Create();

        var set = Parse(await bridge.HandleAsync(Request("r1", "data.set", """{"key":"my.heroes","value":{"k3":{"owned":true}}}""")));
        var get = Parse(await bridge.HandleAsync(Request("r2", "data.get", """{"key":"my.heroes"}""")));
        var list = Parse(await bridge.HandleAsync(Request("r3", "data.list", """{"scope":"squad","prefix":"hero."}""")));

        Assert.Equal("my.heroes", (string?)set["data"]!["key"]);
        Assert.True((bool)get["data"]!["k3"]!["owned"]!);
        Assert.True((bool)list["data"]!["truncated"]!);
        Assert.Equal(1, (int)list["data"]!["items"]![0]!["value"]!);
        Assert.Equal(["set personal my.heroes", "get personal my.heroes", "list squad hero."], api.Calls);
    }

    [Theory]
    [InlineData("""{"key":"..","scope":"personal"}""", "validation_failed")]
    [InlineData("""{"key":"a","scope":"admin"}""", "validation_failed")]
    [InlineData("""{"key":"a","scope":"../../me"}""", "validation_failed")]
    [InlineData("""{"key":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""", "value_too_large")]
    [InlineData("""{"key":5}""", "value_too_large")]
    public async Task Rejects_bad_keys_and_scopes_before_calling_the_api(string parameters, string code)
    {
        var (bridge, api, _, _) = Create();

        var response = await bridge.HandleAsync(Request("r1", "data.get", parameters));

        Assert.Equal(code, ErrorCode(response));
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task Unknown_methods_and_oversized_messages_fail()
    {
        var (bridge, api, _, _) = Create();

        Assert.Equal("validation_failed", ErrorCode(await bridge.HandleAsync(Request("r1", "eval"))));
        var huge = Request("r2", "data.set", $$"""{"key":"a","value":"{{new string('x', ToolBridge.MaxMessageBytes)}}"}""");
        Assert.Equal("value_too_large", ErrorCode(await bridge.HandleAsync(huge)));
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task Api_problems_keep_their_code()
    {
        var (bridge, api, _, _) = Create();
        api.Fail = () => new ApiProblemException(new ApiProblem { Code = "quota_exceeded", Status = 422, Detail = "Zu viel." });

        var response = Parse(await bridge.HandleAsync(Request("r1", "data.set", """{"key":"a","value":1}""")));

        Assert.False((bool)response["ok"]!);
        Assert.Equal("quota_exceeded", (string?)response["error"]!["code"]);
        Assert.Equal("Zu viel.", (string?)response["error"]!["message"]);
    }

    [Fact]
    public async Task At_most_eight_requests_in_flight()
    {
        var (bridge, api, _, _) = Create();
        api.Gate = new TaskCompletionSource();

        var running = Enumerable.Range(0, 8).Select(i => bridge.HandleAsync(Request($"r{i}", "squad"))).ToList();
        var ninth = await bridge.HandleAsync(Request("r9", "squad"));
        api.Gate.SetResult();
        await Task.WhenAll(running);

        Assert.Equal("rate_limited", ErrorCode(ninth));
        Assert.All(running, r => Assert.True((bool)Parse(r.Result)["ok"]!));
    }

    [Fact]
    public async Task Copy_at_most_once_per_second()
    {
        var (bridge, _, host, clock) = Create();

        var first = Parse(await bridge.HandleAsync(Request("r1", "copy", """{"text":"/name ^1R^7oG"}""")));
        var second = await bridge.HandleAsync(Request("r2", "copy", """{"text":"x"}"""));
        clock.Now += TimeSpan.FromSeconds(1);
        var third = Parse(await bridge.HandleAsync(Request("r3", "copy", """{"text":"y"}""")));

        Assert.True((bool)first["ok"]!);
        Assert.Equal("rate_limited", ErrorCode(second));
        Assert.True((bool)third["ok"]!);
        Assert.Equal(["/name ^1R^7oG", "y"], host.Copied);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows")]
    [InlineData("https://user:pw@example.com/")]
    [InlineData("nicht-absolut")]
    public async Task OpenLink_accepts_only_plain_http_links(string url)
    {
        var (bridge, _, host, _) = Create();

        var response = await bridge.HandleAsync(Request("r1", "openLink", $$"""{"url":"{{url}}"}"""));

        Assert.Equal("validation_failed", ErrorCode(response));
        Assert.Empty(host.Opened);
    }

    [Fact]
    public async Task OpenLink_asks_and_limits()
    {
        var (bridge, _, host, clock) = Create();

        Assert.True((bool)Parse(await bridge.HandleAsync(Request("r1", "openLink", """{"url":"https://rebelsofgaming.com/"}""")))["ok"]!);
        Assert.Equal("rate_limited", ErrorCode(await bridge.HandleAsync(Request("r2", "openLink", """{"url":"https://example.com/"}"""))));
        clock.Now += TimeSpan.FromSeconds(2);
        host.Allow = false;
        Assert.Equal("cancelled", ErrorCode(await bridge.HandleAsync(Request("r3", "openLink", """{"url":"https://example.com/"}"""))));

        Assert.Equal([new Uri("https://rebelsofgaming.com/")], host.Opened);
    }

    [Fact]
    public async Task OpenLink_is_blocked_after_five_failures()
    {
        var (bridge, _, host, clock) = Create();

        for (var i = 0; i < 5; i++)
        {
            await bridge.HandleAsync(Request($"f{i}", "openLink", """{"url":"ftp://x"}"""));
        }

        clock.Now += TimeSpan.FromSeconds(5);
        Assert.Equal("rate_limited", ErrorCode(await bridge.HandleAsync(Request("r1", "openLink", """{"url":"https://example.com/"}"""))));
        clock.Now += TimeSpan.FromSeconds(60);
        Assert.True((bool)Parse(await bridge.HandleAsync(Request("r2", "openLink", """{"url":"https://example.com/"}""")))["ok"]!);
        Assert.Single(host.Opened);
    }

    [Fact]
    public async Task Resize_is_clamped()
    {
        var (bridge, _, host, _) = Create();

        Assert.Null(await bridge.HandleAsync("""{"rog":"bridge","v":1,"method":"resize","params":{"height":20000}}"""));
        Assert.Equal(8000, host.Height);
        await bridge.HandleAsync("""{"rog":"bridge","v":1,"method":"resize","params":{"height":10}}""");
        Assert.Equal(80, host.Height);
    }

    [Fact]
    public async Task Assets_only_from_the_tool_list_as_base64()
    {
        var (bridge, api, _, _) = Create();

        var list = Parse(await bridge.HandleAsync(Request("r1", "assets.list")));
        var asset = Parse(await bridge.HandleAsync(Request("r2", "assets.get", """{"name":"heroes/arthur.png"}""")));
        var unknown = await bridge.HandleAsync(Request("r3", "assets.get", """{"name":"../secret.png"}"""));

        Assert.Equal("heroes/arthur.png", (string?)list["data"]![0]!["name"]);
        Assert.Equal(64, (int)list["data"]![0]!["width"]!);
        Assert.Equal("AQID", (string?)asset["data"]!["base64"]);
        Assert.Equal("image/png", (string?)asset["data"]!["mime"]);
        Assert.Equal("not_found", ErrorCode(unknown));
        Assert.Equal(1, api.Calls.Count(c => c == "assets")); // Liste einmal je Tool
        Assert.DoesNotContain(api.Calls, c => c.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Too_large_assets_are_refused()
    {
        var (bridge, api, _, _) = Create();
        api.Assets = [new("big.png", "image/png", ToolBridge.AssetMaxBytes + 1, null, null, null)];

        Assert.Equal("value_too_large", ErrorCode(await bridge.HandleAsync(Request("r1", "assets.get", """{"name":"big.png"}"""))));
        Assert.DoesNotContain(api.Calls, c => c.StartsWith("asset ", StringComparison.Ordinal));
    }
}
