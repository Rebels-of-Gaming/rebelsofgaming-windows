using System.Net.Http.Json;
using System.Text.Json.Nodes;
using RoG.Core.Api;

namespace RoG.Core.Features.Shop;

/// <summary>Preis in Cent; <see cref="Formatted"/> formatiert der Server in der Sprache der Anfrage.</summary>
public sealed record ShopPrice(long Amount, string Currency, string Formatted);

/// <summary>Unterstützer-Rang (rein kosmetisch), 30 Tage oder dauerhaft.</summary>
public sealed record ShopProduct(string Id, string Slug, string Name, string? Description, ShopPrice Price, string? RoleLabel, string? RoleColor, int? DurationDays, bool Permanent, IReadOnlyList<string> Providers);

/// <summary>Freie Spende ohne Gegenleistung: Grenzen in Cent und die Danke-Liste (nur mit Opt-in, nie Beträge).</summary>
public sealed record ShopDonations(long Min, long Max, string Currency, IReadOnlyList<string> Providers, IReadOnlyList<(string Name, string Month)> Supporters);

/// <summary>Eigene Bestellung bzw. Spende; <see cref="ReceiptUrl"/> = Quittungsseite der Website.</summary>
public sealed record ShopOrder(
    string Id,
    string Number,
    string Kind,
    string Status,
    string? FailureReason,
    string ItemName,
    ShopPrice Price,
    string Provider,
    bool Sandbox,
    bool Permanent,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? RefundedAt,
    DateTimeOffset? CreatedAt,
    Uri? ReceiptUrl);

/// <summary>Shop-Stand: <see cref="Enabled"/> = false bei <c>shop_disabled</c> (aus bzw. Testmodus).</summary>
public sealed record ShopOverview(bool Enabled, IReadOnlyList<ShopProduct> Products, ShopDonations? Donations);

/// <summary>
/// Shop &amp; Spenden (Website v0.47.0): Produkte, Spenden-Angaben und eigene Bestellungen nur anzeigen. Bezahlt wird
/// auf der Website (Stripe, PayPal, Tebex) – die App startet keinen Kauf (Microsoft-Store-Regeln für digitale Güter,
/// bis der Owner entscheidet), sie verlinkt nur. Produkte und Spenden auch im Gastmodus, Bestellungen mit <c>shop:read</c>.
/// </summary>
public sealed class ShopService(HttpClient api)
{
    public const string ReadScope = "shop:read";
    public const string KindProduct = "product";
    public const string KindDonation = "donation";

    public async Task<ShopOverview> OverviewAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ShopProduct> products = [];
        var enabled = true;
        try
        {
            var root = await GetAsync("shop/products", cancellationToken).ConfigureAwait(false);
            products = root?["data"] is JsonArray list ? list.Select(ParseProduct).OfType<ShopProduct>().ToList() : [];
        }
        catch (ApiProblemException ex) when (ex.Problem.Code == "shop_disabled")
        {
            enabled = false;
        }

        ShopDonations? donations = null;
        try
        {
            donations = ParseDonations((await GetAsync("shop/donations", cancellationToken).ConfigureAwait(false))?["data"]);
        }
        catch (ApiProblemException ex) when (ex.Problem.Code is "donations_disabled" or "shop_disabled")
        {
            // Spenden aus
        }

        return new ShopOverview(enabled, products, donations);
    }

    /// <summary>Eigene Bestellungen und Spenden, neueste zuerst.</summary>
    public async Task<Page<ShopOrder>> OrdersAsync(string? cursor = null, CancellationToken cancellationToken = default)
    {
        var path = "me/orders?per_page=25" + (string.IsNullOrEmpty(cursor) ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
        var root = await GetAsync(path, cancellationToken).ConfigureAwait(false);
        var items = root?["data"] is JsonArray list ? list.Select(ParseOrder).OfType<ShopOrder>().ToList() : [];
        return new Page<ShopOrder>(items, Str(root?["meta"], "next_cursor"));
    }

    public static ShopProduct? ParseProduct(JsonNode? n) =>
        Str(n, "id") is { } id && ParsePrice(n!["price"]) is { } price
            ? new ShopProduct(
                id,
                Str(n, "slug") ?? id,
                Str(n, "name") ?? id,
                Str(n, "description"),
                price,
                Str(n["role"], "label"),
                Str(n["role"], "color"),
                n["duration_days"] is JsonValue d && d.TryGetValue<int>(out var days) ? days : null,
                Bool(n, "permanent"),
                Providers(n["providers"]))
            : null;

    public static ShopDonations? ParseDonations(JsonNode? n) =>
        n is JsonObject
            ? new ShopDonations(
                Long(n, "min"),
                Long(n, "max"),
                Str(n, "currency") ?? "EUR",
                Providers(n["providers"]),
                n["supporters"] is JsonArray s
                    ? s.Select(x => Str(x, "name") is { } name ? (name, Str(x, "month") ?? string.Empty) : ((string, string)?)null).OfType<(string, string)>().ToList()
                    : [])
            : null;

    public static ShopOrder? ParseOrder(JsonNode? n) =>
        Str(n, "id") is { } id && ParsePrice(n!["price"]) is { } price
            ? new ShopOrder(
                id,
                Str(n, "number") ?? id,
                Str(n, "kind") ?? KindProduct,
                Str(n, "status") ?? "pending",
                Str(n, "failure_reason"),
                Str(n["item"], "name") ?? string.Empty,
                price,
                Str(n, "provider") ?? string.Empty,
                Bool(n, "sandbox"),
                Bool(n["entitlement"], "permanent"),
                ApiValues.Time(Str(n["entitlement"], "expires_at")),
                ApiValues.Time(Str(n, "paid_at")),
                ApiValues.Time(Str(n, "refunded_at")),
                ApiValues.Time(Str(n, "created_at")),
                ApiValues.WebUri(Str(n, "receipt_url")))
            : null;

    private static ShopPrice? ParsePrice(JsonNode? n) =>
        n is JsonObject ? new ShopPrice(Long(n, "amount"), Str(n, "currency") ?? "EUR", Str(n, "formatted") ?? string.Empty) : null;

    private static IReadOnlyList<string> Providers(JsonNode? n) =>
        n is JsonArray a ? a.Select(p => Str(p, "label") ?? Str(p, "key")).OfType<string>().ToList() : [];

    private async Task<JsonNode?> GetAsync(string path, CancellationToken cancellationToken)
    {
        var root = await api.GetFromJsonAsync<JsonNode>(new Uri(path, UriKind.Relative), cancellationToken).ConfigureAwait(false);
        return root;
    }

    private static string? Str(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    private static long Long(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.TryGetValue<long>(out var l) ? l : 0;

    private static bool Bool(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}
