using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RoG.Core.Api;

namespace RoG.Core.Features.Minecraft;

/// <summary>Bonus genau an einem Serientag des Tagesbonus.</summary>
public sealed record DailyMilestone(int Day, int Bonus);

/// <summary>Tagesbonus und Grenzen gegen Missbrauch (<c>GET</c>/<c>PUT /v1/minecraft/economy</c>).</summary>
public sealed record EconomySettings(
    int DailyBase,
    IReadOnlyList<DailyMilestone> Milestones,
    int SystemDailyLimit,
    int KeySystemDailyLimit,
    int KeyEarningsHourlyLimit,
    int PayDailyLimit,
    int AdminDailyLimit);

/// <summary>Verknüpfter Minecraft-Account einer Konto-Wallet.</summary>
public sealed record WalletAccount(string Uuid, string Name, string Edition);

/// <summary>
/// Wallet in der Verwaltung: <c>account</c> (RoG-Konto, alle verknüpften Accounts teilen sie) oder <c>guest</c>
/// (pseudonym über die UUID, <see cref="Uuid"/> nur bei Gästen).
/// </summary>
public sealed record AdminWallet(
    string Id,
    string Kind,
    string? Uuid,
    string? Name,
    string? UserName,
    string? UserSlug,
    IReadOnlyList<WalletAccount> Accounts,
    int Balance,
    MinecraftDaily Daily,
    DateTimeOffset? LastActivityAt)
{
    public bool IsGuest => Kind == MinecraftService.KindGuest;
}

/// <summary>
/// Verwaltung der Rebel-Coins (Website v0.39.0, Guide 46 „Verwaltung“): Recht <c>minecraft.economy</c> (Admin-Recht,
/// 2FA) und Scope <c>minecraft:economy</c>. Rangschutz, eigene Wallet (nur Owner), Tageslimit je Admin und „nie negativ“
/// prüft der Server (<c>403</c>, <c>422 limit_exceeded</c>, <c>409 insufficient_funds</c>).
/// </summary>
public sealed partial class MinecraftService
{
    public const string EconomyScope = "minecraft:economy";
    public const string EconomyPermission = "minecraft.economy";

    public const string KindAccount = "account";
    public const string KindGuest = "guest";

    public const string Credit = "credit";
    public const string Debit = "debit";

    public const int MaxAmount = 1_000_000;
    public const int MaxMilestones = 20;
    public const int MaxMilestoneDay = 3650;
    public const int MinReasonLength = 3;
    public const int MaxReasonLength = 200;

    public static IReadOnlyList<string> WalletKinds { get; } = [KindAccount, KindGuest];

    public async Task<EconomySettings> EconomyAsync(CancellationToken cancellationToken = default) =>
        ParseEconomy((await SendAsync(HttpMethod.Get, "minecraft/economy", null, cancellationToken).ConfigureAwait(false))?["data"]);

    public async Task<EconomySettings> SaveEconomyAsync(EconomySettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var body = new JsonObject
        {
            ["daily_base"] = Math.Clamp(settings.DailyBase, 0, MaxAmount),
            ["daily_milestones"] = new JsonArray(settings.Milestones.Select(m => (JsonNode)new JsonObject { ["day"] = m.Day, ["bonus"] = m.Bonus }).ToArray()),
            ["system_daily_limit"] = Math.Max(0, settings.SystemDailyLimit),
            ["key_system_daily_limit"] = Math.Max(0, settings.KeySystemDailyLimit),
            ["key_earnings_hourly_limit"] = Math.Max(0, settings.KeyEarningsHourlyLimit),
            ["pay_daily_limit"] = Math.Max(0, settings.PayDailyLimit),
            ["admin_daily_limit"] = Math.Max(0, settings.AdminDailyLimit),
        };
        return ParseEconomy((await SendAsync(HttpMethod.Put, "minecraft/economy", body, cancellationToken).ConfigureAwait(false))?["data"]);
    }

    /// <summary>Zuletzt aktive zuerst; Suche nach Spieler-, Kontoname oder UUID, optional nur Konten bzw. Gäste.</summary>
    public async Task<Page<AdminWallet>> WalletsAsync(string? search = null, string? kind = null, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { "per_page=50" };
        if (Limit(search, 64) is { } s)
        {
            query.Add("filter%5Bsearch%5D=" + Uri.EscapeDataString(s));
        }

        if (kind is not null && WalletKinds.Contains(kind))
        {
            query.Add("filter%5Bkind%5D=" + kind);
        }

        if (!string.IsNullOrEmpty(cursor))
        {
            query.Add("cursor=" + Uri.EscapeDataString(cursor));
        }

        var root = await SendAsync(HttpMethod.Get, "minecraft/wallets?" + string.Join('&', query), null, cancellationToken).ConfigureAwait(false);
        var items = root?["data"] is JsonArray list ? list.Select(ParseAdminWallet).OfType<AdminWallet>().ToList() : [];
        return new Page<AdminWallet>(items, Str(root?["meta"], "next_cursor"));
    }

    public async Task<Page<MinecraftTransaction>> WalletTransactionsAsync(string walletId, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var path = $"minecraft/wallets/{Id(walletId)}/transactions?per_page=50";
        if (!string.IsNullOrEmpty(cursor))
        {
            path += "&cursor=" + Uri.EscapeDataString(cursor);
        }

        var root = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        var items = root?["data"] is JsonArray list ? list.Select(ParseTransaction).OfType<MinecraftTransaction>().ToList() : [];
        return new Page<MinecraftTransaction>(items, Str(root?["meta"], "next_cursor"));
    }

    /// <summary>Gutschrift bzw. Abzug mit Pflicht-Grund (Audit); Antwort ist der neue Kontostand.</summary>
    public async Task<int> AdjustAsync(string walletId, string direction, int amount, string reason, CancellationToken cancellationToken = default)
    {
        if (direction is not (Credit or Debit))
        {
            throw new ArgumentException("Invalid direction.", nameof(direction));
        }

        if (!IsValidAmount(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        var r = reason?.Trim() ?? string.Empty;
        if (!IsValidReason(r))
        {
            throw new ArgumentException("Reason must have 3–200 characters.", nameof(reason));
        }

        var body = new JsonObject { ["direction"] = direction, ["amount"] = amount, ["reason"] = r };
        var data = (await SendAsync(HttpMethod.Post, $"minecraft/wallets/{Id(walletId)}/adjustments", body, cancellationToken).ConfigureAwait(false))?["data"];
        return Math.Max(0, IntOf(data, "balance"));
    }

    public static bool IsValidAmount(int amount) => amount is >= 1 and <= MaxAmount;

    public static bool IsValidReason(string? reason) => reason?.Trim().Length is >= MinReasonLength and <= MaxReasonLength;

    public static EconomySettings ParseEconomy(JsonNode? d) => new(
        IntOf(d, "daily_base"),
        d?["daily_milestones"] is JsonArray list
            ? list.Select(m => new DailyMilestone(IntOf(m, "day"), IntOf(m, "bonus"))).Where(m => m.Day > 0).OrderBy(m => m.Day).ToList()
            : [],
        IntOf(d, "system_daily_limit"),
        IntOf(d, "key_system_daily_limit"),
        IntOf(d, "key_earnings_hourly_limit"),
        IntOf(d, "pay_daily_limit"),
        IntOf(d, "admin_daily_limit"));

    public static AdminWallet? ParseAdminWallet(JsonNode? n)
    {
        if (Str(n, "id") is not { } id)
        {
            return null;
        }

        var user = n!["user"];
        var accounts = n["accounts"] is JsonArray list
            ? list.Select(a => Str(a, "uuid") is { } uuid ? new WalletAccount(uuid, Str(a, "name") ?? uuid, Str(a, "edition") ?? Java) : null).OfType<WalletAccount>().ToList()
            : [];
        return new AdminWallet(
            id,
            Str(n, "kind") ?? KindGuest,
            Str(n, "uuid"),
            Str(n, "name"),
            Str(user, "name"),
            Str(user, "slug"),
            accounts,
            Math.Max(0, IntOf(n, "balance")),
            ParseDaily(n["daily"]),
            Time(n, "last_activity_at"));
    }

    /// <summary>„7: 100, 30: 500“ (auch Zeilen oder <c>=</c>) → Serientage; <c>null</c> bei ungültigem Eintrag.</summary>
    public static IReadOnlyList<DailyMilestone>? ParseMilestones(string? text)
    {
        var result = new List<DailyMilestone>();
        foreach (var part in (text ?? string.Empty).Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = MilestonePattern().Match(part);
            if (!m.Success
                || !int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var day)
                || !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var bonus)
                || day is < 1 or > MaxMilestoneDay
                || bonus > MaxAmount
                || result.Any(x => x.Day == day))
            {
                return null;
            }

            result.Add(new DailyMilestone(day, bonus));
        }

        return result.Count > MaxMilestones ? null : result.OrderBy(x => x.Day).ToList();
    }

    public static string FormatMilestones(IEnumerable<DailyMilestone> milestones) =>
        string.Join(", ", milestones.Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.Day}: {m.Bonus}")));

    [GeneratedRegex(@"^(\d{1,4})\s*[:=]\s*(\d{1,7})$")]
    private static partial Regex MilestonePattern();
}
