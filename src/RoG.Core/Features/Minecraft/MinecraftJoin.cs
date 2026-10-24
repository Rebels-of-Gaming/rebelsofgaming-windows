using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RoG.Core.Features.Minecraft;

/// <summary>Adresse und Port einer Edition.</summary>
public sealed record JoinEndpoint(string Address, int Port);

/// <summary>
/// „So kommst du auf den Server“ (Website v0.43.0): Java, Bedrock und – bei <see cref="ConsoleEnabled"/> – der Gamertag
/// für Konsolen (Freund hinzufügen, dann über „beitretbare Welten“). <see cref="Hint"/> in der App-Sprache;
/// <see cref="Hints"/> (alle Sprachen) nur in der Antwort auf das Ändern.
/// </summary>
public sealed record MinecraftJoin(
    JoinEndpoint Java,
    JoinEndpoint Bedrock,
    bool ConsoleEnabled,
    string? Gamertag,
    string? Hint,
    IReadOnlyDictionary<string, string>? Hints = null)
{
    /// <summary>Java-Adresse zum Kopieren – mit Port nur, wenn er vom Standard 25565 abweicht.</summary>
    public string JavaText => MinecraftService.ShowJavaPort(Java.Port) ? $"{Java.Address}:{Java.Port}" : Java.Address;
}

/// <summary>Änderung der Beitrittsdaten – nur gesetzte Felder werden gesendet; <see cref="Hints"/> je Sprache (leer = kein Hinweis).</summary>
public sealed record MinecraftJoinUpdate(
    string? JavaAddress = null,
    int? JavaPort = null,
    string? BedrockAddress = null,
    int? BedrockPort = null,
    bool? ConsoleEnabled = null,
    string? Gamertag = null,
    IReadOnlyDictionary<string, string?>? Hints = null);

public sealed partial class MinecraftService
{
    public const int DefaultJavaPort = 25565;
    public const int DefaultBedrockPort = 19132;
    public const int HintMaxLength = 500;

    /// <summary>Sprachen der Konsolen-Hinweise.</summary>
    public static IReadOnlyList<string> HintLocales { get; } = ["de", "en", "fr"];

    /// <summary>Java-Port nur anzeigen, wenn er vom Standard abweicht.</summary>
    public static bool ShowJavaPort(int port) => port != DefaultJavaPort;

    /// <summary>Hostname oder IPv4 ohne Schema, Pfad oder Port (wie die Website).</summary>
    public static bool IsValidAddress(string? address) => address is not null && AddressPattern().IsMatch(address.Trim());

    public static bool IsValidPort(int? port) => port is >= 1 and <= 65535;

    /// <summary>Xbox-Gamertag: bis 15 Zeichen, optional mit Suffix <c>#1234</c>.</summary>
    public static bool IsValidGamertag(string? gamertag) => gamertag is not null && GamertagPattern().IsMatch(gamertag.Trim());

    /// <summary>Beitrittsdaten – öffentlich (Gastmodus), mit Token Scope <c>servers:read</c>.</summary>
    public async Task<MinecraftJoin?> JoinAsync(CancellationToken cancellationToken = default) =>
        ParseJoin((await SendAsync(HttpMethod.Get, "minecraft/join", null, cancellationToken).ConfigureAwait(false))?["data"]);

    /// <summary>Beitrittsdaten ändern (<c>minecraft:manage</c> + Recht <c>minecraft.manage</c>); Antwort mit den Hinweisen aller Sprachen.</summary>
    public async Task<MinecraftJoin?> UpdateJoinAsync(MinecraftJoinUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var body = new JsonObject();
        if (update.JavaAddress is { } java)
        {
            body["java_address"] = java.Trim();
        }

        if (update.JavaPort is { } javaPort)
        {
            body["java_port"] = javaPort;
        }

        if (update.BedrockAddress is { } bedrock)
        {
            body["bedrock_address"] = bedrock.Trim();
        }

        if (update.BedrockPort is { } bedrockPort)
        {
            body["bedrock_port"] = bedrockPort;
        }

        if (update.ConsoleEnabled is { } enabled)
        {
            body["console_enabled"] = enabled;
        }

        if (update.Gamertag is { } gamertag)
        {
            body["console_gamertag"] = gamertag.Trim();
        }

        if (update.Hints is { } hints)
        {
            var obj = new JsonObject();
            foreach (var locale in HintLocales)
            {
                if (hints.TryGetValue(locale, out var hint))
                {
                    obj[locale] = Limit(hint, HintMaxLength) ?? string.Empty;
                }
            }

            body["console_hint"] = obj;
        }

        return ParseJoin((await SendAsync(HttpMethod.Put, "minecraft/join", body, cancellationToken).ConfigureAwait(false))?["data"]);
    }

    /// <summary>
    /// Aktuelle Werte samt Hinweisen aller Sprachen fürs Formular. Ab dem Website-Release nach v0.43.0 liefert <c>GET</c>
    /// für Verwaltende <c>console.hints</c> mit (API-Lücke W23); fehlt es noch, ein leerer <c>PUT</c> als Übergang
    /// (die Website speichert nur echte Änderungen, also kein Audit).
    /// </summary>
    public async Task<MinecraftJoin?> JoinForEditAsync(CancellationToken cancellationToken = default)
    {
        var join = await JoinAsync(cancellationToken).ConfigureAwait(false);
        return join?.Hints is not null ? join : await UpdateJoinAsync(new MinecraftJoinUpdate(), cancellationToken).ConfigureAwait(false);
    }

    internal static MinecraftJoin? ParseJoin(JsonNode? n)
    {
        if (Endpoint(n?["java"], DefaultJavaPort) is not { } java || Endpoint(n?["bedrock"], DefaultBedrockPort) is not { } bedrock)
        {
            return null;
        }

        var console = n?["console"];
        var enabled = Bool(console, "enabled");
        Dictionary<string, string>? hints = null;
        if (console?["hints"] is JsonObject h)
        {
            hints = HintLocales.ToDictionary(l => l, l => Str(h, l) ?? string.Empty);
        }

        return new MinecraftJoin(java, bedrock, enabled, enabled ? Str(console, "gamertag") : null, enabled ? Str(console, "hint") : null, hints);
    }

    private static JoinEndpoint? Endpoint(JsonNode? n, int fallbackPort)
    {
        if (Str(n, "address") is not { } address)
        {
            return null;
        }

        var port = IntOf(n, "port");
        return new JoinEndpoint(address, IsValidPort(port) ? port : fallbackPort);
    }

    [GeneratedRegex("^(?=.{1,253}$)[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)*$")]
    private static partial Regex AddressPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 ]{0,14}(?:#[0-9]{1,4})?$")]
    private static partial Regex GamertagPattern();
}
