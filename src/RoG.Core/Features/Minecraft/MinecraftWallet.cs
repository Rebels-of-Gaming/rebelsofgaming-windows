using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoG.Core.Features.Minecraft;

/// <summary>Tagesbonus-Serie; <see cref="Streak"/> ist <c>0</c>, wenn die Serie unterbrochen ist.</summary>
public sealed record MinecraftDaily(int Streak, int BestStreak, bool ClaimedToday, DateTimeOffset? NextAt);

/// <summary>
/// Buchung der eigenen Wallet. <see cref="Direction"/>: <c>in</c>, <c>out</c>, <c>internal</c> (zwischen eigenen
/// Accounts); <see cref="Counterparty"/>: <c>system</c>, <c>player</c>, <c>self</c>, <c>removed</c> – andere Spieler
/// werden nie genannt.
/// </summary>
public sealed record MinecraftTransaction(
    string Id,
    string Type,
    string Direction,
    string Counterparty,
    int Amount,
    string? Reason,
    string? Source,
    string? Server,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? CreatedAt)
{
    public bool IsIncoming => Direction == MinecraftService.DirectionIn;

    public bool IsOutgoing => Direction == MinecraftService.DirectionOut;

    /// <summary>Betrag mit Vorzeichen aus Sicht der Wallet (intern = 0).</summary>
    public int SignedAmount => IsIncoming ? Amount : IsOutgoing ? -Amount : 0;
}

/// <summary>Antwort von <c>GET /v1/me/minecraft/wallet</c>: Kontostand, Tagesbonus, die letzten 20 Buchungen.</summary>
public sealed record MinecraftWallet(int Balance, MinecraftDaily Daily, IReadOnlyList<MinecraftTransaction> Transactions);

/// <summary>
/// Rebel-Coins (Website v0.39.0, Guide 46): Währung nur für das Minecraft-Netzwerk – Website und App zeigen sie nur an.
/// Eigene Wallet mit <c>minecraft:read</c>; ob Kontostand und Serie im öffentlichen Profil stehen, steuert das
/// Profilfeld <c>visibility.coins</c> (Standard <c>private</c> = Opt-in, <c>profile:read</c>/<c>profile:write</c>).
/// </summary>
public sealed partial class MinecraftService
{
    public const string DirectionIn = "in";
    public const string DirectionOut = "out";
    public const string DirectionInternal = "internal";

    public const string VisibilityPrivate = "private";
    public const string VisibilityMembers = "members";
    public const string VisibilityPublic = "public";

    /// <summary>Werte für „Rebel-Coins im Profil“, Standard zuerst.</summary>
    public static IReadOnlyList<string> CoinsVisibilities { get; } = [VisibilityPrivate, VisibilityMembers, VisibilityPublic];

    public async Task<MinecraftWallet> WalletAsync(CancellationToken cancellationToken = default)
    {
        var data = (await SendAsync(HttpMethod.Get, "me/minecraft/wallet", null, cancellationToken).ConfigureAwait(false))?["data"];
        return ParseWallet(data);
    }

    public async Task<string> CoinsVisibilityAsync(CancellationToken cancellationToken = default)
    {
        var data = (await SendAsync(HttpMethod.Get, "me/profile", null, cancellationToken).ConfigureAwait(false))?["data"];
        return ParseCoinsVisibility(data);
    }

    /// <summary>Ändert nur <c>visibility.coins</c> – alle anderen Profilfelder bleiben, wie sie sind.</summary>
    public async Task<string> SetCoinsVisibilityAsync(string visibility, CancellationToken cancellationToken = default)
    {
        if (!CoinsVisibilities.Contains(visibility))
        {
            throw new ArgumentException("Invalid visibility.", nameof(visibility));
        }

        var body = new JsonObject { ["visibility"] = new JsonObject { ["coins"] = visibility } };
        var data = (await SendAsync(new HttpMethod("PATCH"), "me/profile", body, cancellationToken).ConfigureAwait(false))?["data"];
        return data?["visibility"] is null ? visibility : ParseCoinsVisibility(data);
    }

    /// <summary>Unbekannte oder fehlende Werte gelten als <c>private</c> (Standard des Servers).</summary>
    public static string ParseCoinsVisibility(JsonNode? profile) =>
        Str(profile?["visibility"], "coins") is { } v && CoinsVisibilities.Contains(v) ? v : VisibilityPrivate;

    /// <summary>Ohne Wallet liefert der Server <c>balance: 0</c> und keine Buchungen.</summary>
    public static MinecraftWallet ParseWallet(JsonNode? data) => new(
        Math.Max(0, IntOf(data, "balance")),
        ParseDaily(data?["daily"]),
        data?["transactions"] is JsonArray list ? list.Select(ParseTransaction).OfType<MinecraftTransaction>().ToList() : []);

    public static MinecraftDaily ParseDaily(JsonNode? n) => new(
        Math.Max(0, IntOf(n, "streak")),
        Math.Max(0, IntOf(n, "best_streak")),
        n?["claimed_today"] is JsonValue v && v.TryGetValue<bool>(out var claimed) && claimed,
        Time(n, "next_at"));

    public static MinecraftTransaction? ParseTransaction(JsonNode? n)
    {
        if (Str(n, "id") is not { } id || Str(n, "type") is not { } type)
        {
            return null;
        }

        // Verwaltung liefert server als Objekt {id, name}, die eigene Wallet als Text.
        var server = n!["server"] is JsonObject s ? Str(s, "name") : Str(n, "server");
        return new MinecraftTransaction(
            id,
            type,
            Str(n, "direction") ?? DirectionInternal,
            Str(n, "counterparty") ?? "system",
            Math.Max(0, IntOf(n, "amount")),
            Str(n, "reason"),
            Str(n, "source"),
            server,
            Time(n, "period_start"),
            Time(n, "created_at"));
    }

    private static int IntOf(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<long>(out var l)
            ? (int)Math.Clamp(l, int.MinValue, int.MaxValue)
            : 0;
}
