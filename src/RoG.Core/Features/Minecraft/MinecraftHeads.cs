using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RoG.Core.Api;

namespace RoG.Core.Features.Minecraft;

/// <summary>
/// Kopf im Katalog „RoG-Heads“: Namen je Sprache (DE Pflicht), Kategorie, Textur-Hash, Tags, Preis in Rebel-Coins
/// (<c>null</c> = Kategoriepreis). <see cref="ImageUrl"/> erzeugt die Website selbst (nie von Mojang geladen).
/// </summary>
public sealed record MinecraftHead(
    int Id,
    string NameDe,
    string? NameEn,
    string? NameFr,
    string Category,
    string Texture,
    IReadOnlyList<string> Tags,
    int? Price,
    Uri? ImageUrl,
    string ImageStatus,
    string? Creator,
    DateTimeOffset? CreatedAt)
{
    /// <summary>Name in der App-Sprache, sonst Deutsch.</summary>
    public string Name(string? language) => language switch
    {
        "en" when NameEn is { Length: > 0 } en => en,
        "fr" when NameFr is { Length: > 0 } fr => fr,
        _ => NameDe,
    };
}

/// <summary>Änderung eines Kopfs; nur gesetzte Felder werden geschickt (<see cref="ClearEn"/>/<see cref="ClearFr"/> leeren).</summary>
public sealed record MinecraftHeadEdit(
    string? NameDe = null,
    string? NameEn = null,
    string? NameFr = null,
    bool ClearEn = false,
    bool ClearFr = false,
    string? Category = null,
    IReadOnlyList<string>? Tags = null,
    int? Price = null,
    bool CategoryPrice = false);

/// <summary>
/// Kopf-Katalog in der Verwaltung (Website v0.48.0, <c>/v1/minecraft/heads</c>): suchen, nach Kategorie filtern,
/// anlegen (per Textur-Hash), bearbeiten, löschen. Recht <c>minecraft.manage</c> + Scope <c>minecraft:manage</c>.
/// Das Plugin holt den Katalog alle 5 Minuten.
/// </summary>
public sealed partial class MinecraftService
{
    public static IReadOnlyList<string> HeadCategories { get; } = ["alphabet", "animals", "blocks", "decoration", "food", "humans", "monsters", "plants", "misc", "rog"];

    public const int HeadNameMaxLength = 48;
    public const int HeadTagsMax = 10;
    public const int HeadTagMaxLength = 24;
    public const int HeadPriceMax = 1_000_000;

    public async Task<Page<MinecraftHead>> HeadsAsync(string? category = null, string? search = null, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { "per_page=50" };
        if (category is not null && HeadCategories.Contains(category))
        {
            query.Add("filter[category]=" + category);
        }

        if (Limit(search, 64) is { } s)
        {
            query.Add("filter[search]=" + Uri.EscapeDataString(s));
        }

        if (!string.IsNullOrEmpty(cursor))
        {
            query.Add("cursor=" + Uri.EscapeDataString(cursor));
        }

        var root = await SendAsync(HttpMethod.Get, "minecraft/heads?" + string.Join('&', query), null, cancellationToken).ConfigureAwait(false);
        var items = root?["data"] is JsonArray list ? list.Select(ParseHead).OfType<MinecraftHead>().ToList() : [];
        return new Page<MinecraftHead>(items, Str(root?["meta"], "next_cursor"));
    }

    /// <summary>Neuer Kopf; vorhandene Textur → <c>409 head_exists</c>.</summary>
    public async Task<MinecraftHead?> CreateHeadAsync(string texture, string nameDe, string? nameEn, string? nameFr, string category, IReadOnlyList<string> tags, int? price, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["name"] = new JsonObject { ["de"] = nameDe.Trim(), ["en"] = Limit(nameEn, HeadNameMaxLength), ["fr"] = Limit(nameFr, HeadNameMaxLength) },
            ["category"] = category,
            ["texture"] = NormalizeTexture(texture),
            ["tags"] = new JsonArray(NormalizeTags(tags).Select(t => (JsonNode)t).ToArray()),
            ["price"] = price,
        };
        return ParseHead((await SendAsync(HttpMethod.Post, "minecraft/heads", body, cancellationToken).ConfigureAwait(false))?["data"]);
    }

    /// <summary>Nur übergebene Felder; ohne Änderung kein Aufruf (<c>null</c>). Die Textur ist fest.</summary>
    public async Task<MinecraftHead?> UpdateHeadAsync(int id, MinecraftHeadEdit edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var body = BuildHeadPatch(edit);
        return body.Count == 0
            ? null
            : ParseHead((await SendAsync(HttpMethod.Patch, "minecraft/heads/" + id.ToString(System.Globalization.CultureInfo.InvariantCulture), body, cancellationToken).ConfigureAwait(false))?["data"]);
    }

    public Task DeleteHeadAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, "minecraft/heads/" + id.ToString(System.Globalization.CultureInfo.InvariantCulture), null, cancellationToken);

    public static JsonObject BuildHeadPatch(MinecraftHeadEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var body = new JsonObject();
        var name = new JsonObject();
        if (Limit(edit.NameDe, HeadNameMaxLength) is { } de)
        {
            name["de"] = de;
        }

        if (edit.ClearEn)
        {
            name["en"] = null;
        }
        else if (Limit(edit.NameEn, HeadNameMaxLength) is { } en)
        {
            name["en"] = en;
        }

        if (edit.ClearFr)
        {
            name["fr"] = null;
        }
        else if (Limit(edit.NameFr, HeadNameMaxLength) is { } fr)
        {
            name["fr"] = fr;
        }

        if (name.Count > 0)
        {
            body["name"] = name;
        }

        if (edit.Category is { } c && HeadCategories.Contains(c))
        {
            body["category"] = c;
        }

        if (edit.Tags is { } tags)
        {
            body["tags"] = new JsonArray(NormalizeTags(tags).Select(t => (JsonNode)t).ToArray());
        }

        if (edit.CategoryPrice)
        {
            body["price"] = null;
        }
        else if (edit.Price is { } p)
        {
            body["price"] = Math.Clamp(p, 1, HeadPriceMax);
        }

        return body;
    }

    /// <summary>Textur-Hash: 40–64 Hex-Zeichen, klein; auch aus einer Adresse <c>textures.minecraft.net/texture/&lt;hash&gt;</c>.</summary>
    public static string? NormalizeTexture(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        var slash = v.LastIndexOf('/');
        if (slash >= 0)
        {
            v = v[(slash + 1)..];
        }

        v = v.ToLowerInvariant();
        return TexturePattern().IsMatch(v) ? v : null;
    }

    /// <summary>Höchstens 10 Tags, je ≤ 24 Zeichen aus <c>a–z 0–9 ä ö ü ß - _</c>, klein, ohne doppelte.</summary>
    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string>? tags) =>
        (tags ?? []).Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length is > 0 and <= HeadTagMaxLength && TagPattern().IsMatch(t))
            .Distinct(StringComparer.Ordinal)
            .Take(HeadTagsMax)
            .ToList();

    /// <summary>Kommagetrennte Eingabe → Tags.</summary>
    public static IReadOnlyList<string> ParseTags(string? text) =>
        NormalizeTags((text ?? string.Empty).Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Name ist Klartext: keine Steuerzeichen, keine <c>§</c>-Farbcodes, keine MiniMessage-Tags.</summary>
    public static bool IsValidHeadName(string? name) =>
        name is { Length: > 0 } n && n.Trim().Length is > 0 and <= HeadNameMaxLength && !n.Any(char.IsControl) && !n.Contains('§') && !MiniMessagePattern().IsMatch(n);

    public static MinecraftHead? ParseHead(JsonNode? n)
    {
        if (IntOf(n, "id") is var id && id <= 0 || Str(n, "texture") is not { } texture)
        {
            return null;
        }

        var name = n!["name"];
        var creator = n["creator"];
        return new MinecraftHead(
            id,
            Str(name, "de") ?? texture,
            Str(name, "en"),
            Str(name, "fr"),
            Str(n, "category") ?? "misc",
            texture,
            n["tags"] is JsonArray tags ? tags.Select(t => t?.GetValue<string>()).OfType<string>().ToList() : [],
            n["price"] is JsonValue pv && pv.TryGetValue<int>(out var price) ? price : null,
            ApiValues.WebUri(Str(n, "image_url")),
            Str(n, "image_status") ?? "pending",
            Str(creator, "name"),
            Time(n, "created_at"));
    }

    [GeneratedRegex("^[0-9a-f]{40,64}$")]
    private static partial Regex TexturePattern();

    [GeneratedRegex("^[a-z0-9äöüß_-]+$")]
    private static partial Regex TagPattern();

    [GeneratedRegex("<[^<>]+>")]
    private static partial Regex MiniMessagePattern();
}
