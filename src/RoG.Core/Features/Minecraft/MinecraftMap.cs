using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoG.Core.Features.Minecraft;

/// <summary>Warp o. ä. auf einer Kartenwelt (Blockkoordinaten).</summary>
public sealed record MapMarker(string Type, string Id, string Label, int X, int Z);

/// <summary>
/// Kartenwelt eines Servers. <see cref="TileUrl"/> ist das Adress-Schema mit <c>{zoom}</c>/<c>{x}</c>/<c>{z}</c>/<c>{version}</c>;
/// welche Kacheln es gibt, verrät der Kachel-Index je Region. <see cref="Hidden"/>, <see cref="Names"/>, <see cref="Bytes"/>
/// nur für Verwaltende.
/// </summary>
public sealed record MapWorld(
    string Id,
    string World,
    string Name,
    string Environment,
    bool Loaded,
    int SpawnX,
    int SpawnZ,
    int ZoomLevels,
    int TileCount,
    int RegionSize,
    string? TileUrl,
    DateTimeOffset? TilesUpdatedAt,
    IReadOnlyList<MapMarker> Markers,
    DateTimeOffset? ReportedAt,
    bool Hidden = false,
    IReadOnlyDictionary<string, string>? Names = null,
    long? Bytes = null);

/// <summary>Server auf der Karte; <see cref="Id"/> = Slug (Verwaltung), <see cref="NetworkId"/> = Heartbeat-ID (Live-Spieler).</summary>
public sealed record MapServer(string Id, string? NetworkId, string Name, IReadOnlyList<MapWorld> Worlds, bool Hidden = false, bool? IsPublic = null, long? Bytes = null);

/// <summary>Speicher der Kacheln (nur Verwaltende).</summary>
public sealed record MapQuota(long Used, long Total, long WarnAt, long PerServer, bool Warned);

/// <summary>
/// Übersicht der Weltkarte. <see cref="LivePlayers"/> = dieser Betrachter sieht Live-Spieler; <see cref="LiveMode"/> =
/// Einstellung (<c>off</c>, <c>auth</c>, <c>public</c>).
/// </summary>
public sealed record MapOverview(bool LivePlayers, string LiveMode, IReadOnlyList<MapServer> Servers, MapQuota? Quota);

/// <summary>Vorhandene Kachel einer Region.</summary>
public readonly record struct MapTileRef(int X, int Z, string Version);

/// <summary>Live-Spieler (Position in Blöcken, Blickrichtung in Grad); <see cref="HeadUrl"/> nur aus dem eigenen Speicher.</summary>
public sealed record MapLivePlayer(string Uuid, string Name, string World, string? WorldId, double X, double Z, double? Yaw, bool Bedrock, string? HeadUrl);

public sealed record MapLive(string Server, bool Enabled, DateTimeOffset? UpdatedAt, IReadOnlyList<MapLivePlayer> Players);

/// <summary>Link aus dem Spiel bzw. von der Website: <c>/minecraft/karte?server=&amp;world=&amp;x=&amp;z=</c>.</summary>
public sealed record MapLink(string? Server, string? World, int? X, int? Z)
{
    /// <summary>Als Routen-Schlüssel (Query ohne <c>?</c>); leer, wenn nichts gesetzt ist.</summary>
    public string ToKey()
    {
        var parts = new List<string>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                parts.Add(name + "=" + Uri.EscapeDataString(value));
            }
        }

        Add("server", Server);
        Add("world", World);
        Add("x", X?.ToString(CultureInfo.InvariantCulture));
        Add("z", Z?.ToString(CultureInfo.InvariantCulture));
        return string.Join('&', parts);
    }

    /// <summary>Aus einer Query (mit oder ohne <c>?</c>); ungültige Werte fallen weg.</summary>
    public static MapLink Parse(string? query)
    {
        string? server = null, world = null;
        int? x = null, z = null;
        foreach (var pair in (query ?? string.Empty).TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var value = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' ')).Trim();
            switch (pair[..eq])
            {
                case "server" when MinecraftMapGeometry.IsValidName(value):
                    server = value;
                    break;
                case "world" when MinecraftMapGeometry.IsValidName(value):
                    world = value;
                    break;
                case "x":
                    x = Coordinate(value);
                    break;
                case "z":
                    z = Coordinate(value);
                    break;
            }
        }

        return new MapLink(server, world, x, z);
    }

    private static int? Coordinate(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && Math.Abs(d) <= MinecraftMapGeometry.WorldBorder
            ? (int)Math.Floor(d)
            : null;
}

/// <summary>
/// Kachel-Rechnung der Weltkarte (Guide 48): Kachel 512 × 512 px, Zoom n = 2ⁿ Blöcke je Pixel, Kachel-Index
/// <c>floor(block / (512 · 2ⁿ))</c>, Regionen zu <c>region_size</c> × <c>region_size</c> Kacheln, Norden (−z) oben.
/// </summary>
public static class MinecraftMapGeometry
{
    public const int TileSize = 512;
    public const int WorldBorder = 30_000_000;
    public const int MaxZoomLevels = 8;
    public const int DefaultRegionSize = 32;

    /// <summary>Stärkste Vergrößerung: 8 Bildschirm-Pixel je Block (Kacheln der Stufe 0 hochskaliert).</summary>
    public const double MaxPixelsPerBlock = 8;

    /// <summary>Blöcke je Kachel-Kante auf Stufe <paramref name="zoom"/>.</summary>
    public static long BlocksPerTile(int zoom) => (long)TileSize << Math.Clamp(zoom, 0, MaxZoomLevels);

    public static int TileIndex(double block, int zoom) => (int)Math.Floor(block / BlocksPerTile(zoom));

    public static int RegionIndex(int tile, int regionSize) => (int)Math.Floor(tile / (double)Math.Max(1, regionSize));

    /// <summary>Größter Kachel-Index je Richtung (Weltgrenze).</summary>
    public static int MaxTileIndex(int zoom) => (int)Math.Ceiling(WorldBorder / (double)BlocksPerTile(zoom));

    /// <summary>Größter Regions-Index je Richtung, wie die Website prüft.</summary>
    public static int MaxRegionIndex(int zoom, int regionSize) => (MaxTileIndex(zoom) / Math.Max(1, regionSize)) + 1;

    /// <summary>Schwächste Vergrößerung: gröbste Stufe halb verkleinert.</summary>
    public static double MinPixelsPerBlock(int zoomLevels) => 0.5 / (1 << Math.Clamp(zoomLevels, 0, MaxZoomLevels));

    /// <summary>
    /// Passende Kachel-Stufe für eine Vergrößerung: die Stufe, deren Auflösung am nächsten liegt (bei 1 px/Block Stufe 0,
    /// bei 0,5 Stufe 1 …), begrenzt auf 0 … <paramref name="zoomLevels"/>.
    /// </summary>
    public static int ZoomFor(double pixelsPerBlock, int zoomLevels)
    {
        if (pixelsPerBlock <= 0 || double.IsNaN(pixelsPerBlock))
        {
            return Math.Clamp(zoomLevels, 0, MaxZoomLevels);
        }

        var n = (int)Math.Round(Math.Log2(1 / pixelsPerBlock));
        return Math.Clamp(n, 0, Math.Clamp(zoomLevels, 0, MaxZoomLevels));
    }

    /// <summary>Adresse einer Kachel aus dem Schema der Website; ohne Schema oder Version <c>null</c>.</summary>
    public static Uri? TileUri(string? template, int zoom, int x, int z, string? version)
    {
        if (string.IsNullOrEmpty(template) || string.IsNullOrEmpty(version) || !IsVersion(version))
        {
            return null;
        }

        var url = template
            .Replace("{zoom}", zoom.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{x}", x.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{z}", z.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{version}", version, StringComparison.Ordinal);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;
    }

    /// <summary>Welt-/Servernamen aus Links: <c>[A-Za-z0-9_.:-]</c>, 1–64 Zeichen.</summary>
    public static bool IsValidName(string? value) =>
        value is { Length: > 0 and <= 64 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or ':' or '-');

    /// <summary>Speichergröße für Quoten (bis GB): „512 KB“, „3,4 MB“, „50 GB“.</summary>
    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{Math.Max(0, bytes)} B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture) + " KB",
        < 1024L * 1024 * 1024 => (bytes / 1048576.0).ToString("0.#", CultureInfo.CurrentCulture) + " MB",
        _ => (bytes / 1073741824.0).ToString("0.#", CultureInfo.CurrentCulture) + " GB",
    };

    private static bool IsVersion(string version) => version.Length <= 64 && version.All(char.IsAsciiHexDigit);
}

/// <summary>
/// Sichtbereich der Karte: Mitte in Blöcken, Vergrößerung in Pixel je Block, Größe in Pixeln. Rechnet Block ↔ Bildschirm,
/// wählt die Kachel-Stufe und liefert die sichtbaren Kacheln und Regionen. Norden (−z) oben.
/// </summary>
public sealed class MapViewport
{
    public MapViewport(int zoomLevels)
    {
        ZoomLevels = Math.Clamp(zoomLevels, 0, MinecraftMapGeometry.MaxZoomLevels);
        PixelsPerBlock = 1;
    }

    public int ZoomLevels { get; }

    public double CenterX { get; private set; }

    public double CenterZ { get; private set; }

    public double PixelsPerBlock { get; private set; }

    public double Width { get; private set; }

    public double Height { get; private set; }

    /// <summary>Kachel-Stufe für die aktuelle Vergrößerung.</summary>
    public int TileZoom => MinecraftMapGeometry.ZoomFor(PixelsPerBlock, ZoomLevels);

    public bool CanZoomIn => PixelsPerBlock < MinecraftMapGeometry.MaxPixelsPerBlock;

    public bool CanZoomOut => PixelsPerBlock > MinecraftMapGeometry.MinPixelsPerBlock(ZoomLevels);

    public void Resize(double width, double height)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
    }

    public void CenterOn(double x, double z)
    {
        CenterX = Math.Clamp(x, -MinecraftMapGeometry.WorldBorder, MinecraftMapGeometry.WorldBorder);
        CenterZ = Math.Clamp(z, -MinecraftMapGeometry.WorldBorder, MinecraftMapGeometry.WorldBorder);
    }

    public void SetScale(double pixelsPerBlock) =>
        PixelsPerBlock = Math.Clamp(pixelsPerBlock, MinecraftMapGeometry.MinPixelsPerBlock(ZoomLevels), MinecraftMapGeometry.MaxPixelsPerBlock);

    /// <summary>Verschieben um Bildschirm-Pixel (Ziehen nach rechts zeigt mehr vom Westen).</summary>
    public void Pan(double dx, double dy) => CenterOn(CenterX - (dx / PixelsPerBlock), CenterZ - (dy / PixelsPerBlock));

    /// <summary>Vergrößern um <paramref name="factor"/>, der Block unter (<paramref name="sx"/>, <paramref name="sy"/>) bleibt stehen.</summary>
    public void ZoomAt(double factor, double sx, double sy)
    {
        var (bx, bz) = ScreenToBlock(sx, sy);
        SetScale(PixelsPerBlock * factor);
        CenterOn(bx - ((sx - (Width / 2)) / PixelsPerBlock), bz - ((sy - (Height / 2)) / PixelsPerBlock));
    }

    public (double X, double Y) BlockToScreen(double x, double z) =>
        (((x - CenterX) * PixelsPerBlock) + (Width / 2), ((z - CenterZ) * PixelsPerBlock) + (Height / 2));

    public (double X, double Z) ScreenToBlock(double sx, double sy) =>
        (CenterX + ((sx - (Width / 2)) / PixelsPerBlock), CenterZ + ((sy - (Height / 2)) / PixelsPerBlock));

    /// <summary>Linke obere Ecke und Kantenlänge einer Kachel der Stufe <paramref name="zoom"/> auf dem Bildschirm.</summary>
    public (double Left, double Top, double Size) TileRect(int zoom, int x, int z)
    {
        var blocks = MinecraftMapGeometry.BlocksPerTile(zoom);
        var (left, top) = BlockToScreen(x * (double)blocks, z * (double)blocks);
        return (left, top, blocks * PixelsPerBlock);
    }

    /// <summary>Sichtbare Kacheln der aktuellen Stufe (innerhalb der Weltgrenze), Mitte zuerst.</summary>
    public IReadOnlyList<(int X, int Z)> VisibleTiles()
    {
        if (Width <= 0 || Height <= 0)
        {
            return [];
        }

        var zoom = TileZoom;
        var max = MinecraftMapGeometry.MaxTileIndex(zoom);
        var (x0, z0) = ScreenToBlock(0, 0);
        var (x1, z1) = ScreenToBlock(Width, Height);
        var minX = Math.Max(-max, MinecraftMapGeometry.TileIndex(x0, zoom));
        var maxX = Math.Min(max, MinecraftMapGeometry.TileIndex(x1, zoom));
        var minZ = Math.Max(-max, MinecraftMapGeometry.TileIndex(z0, zoom));
        var maxZ = Math.Min(max, MinecraftMapGeometry.TileIndex(z1, zoom));
        var cx = MinecraftMapGeometry.TileIndex(CenterX, zoom);
        var cz = MinecraftMapGeometry.TileIndex(CenterZ, zoom);
        var tiles = new List<(int X, int Z)>();
        for (var x = minX; x <= maxX; x++)
        {
            for (var z = minZ; z <= maxZ; z++)
            {
                tiles.Add((x, z));
            }
        }

        return tiles.OrderBy(t => Math.Abs(t.X - cx) + Math.Abs(t.Z - cz)).ToList();
    }

    /// <summary>Regionen (Kachel-Index), die die sichtbaren Kacheln abdecken.</summary>
    public IReadOnlyList<(int X, int Z)> VisibleRegions(int regionSize)
    {
        var max = MinecraftMapGeometry.MaxRegionIndex(TileZoom, regionSize);
        return VisibleTiles()
            .Select(t => (X: MinecraftMapGeometry.RegionIndex(t.X, regionSize), Z: MinecraftMapGeometry.RegionIndex(t.Z, regionSize)))
            .Where(r => Math.Abs(r.X) <= max && Math.Abs(r.Z) <= max)
            .Distinct()
            .ToList();
    }
}

public sealed partial class MinecraftService
{
    public const string LiveOff = "off";
    public const string LiveAuth = "auth";
    public const string LivePublic = "public";

    /// <summary>Live-Spieler alle 5 s abfragen (Guide 48).</summary>
    public static TimeSpan LiveInterval { get; } = TimeSpan.FromSeconds(5);

    public static IReadOnlyList<string> LiveModes { get; } = [LiveOff, LiveAuth, LivePublic];

    public const int MapNameMaxLength = 64;

    /// <summary>Server und Welten der Karte (öffentlich; Verwaltende zusätzlich Ausgeblendetes, Speicher und Namen je Sprache).</summary>
    public async Task<MapOverview> MapAsync(CancellationToken cancellationToken = default) =>
        ParseMap((await SendAsync(HttpMethod.Get, "minecraft/map", null, cancellationToken).ConfigureAwait(false))?["data"]);

    /// <summary>Vorhandene Kacheln einer Region (32 × 32 Kacheln) der Stufe <paramref name="zoom"/>.</summary>
    public async Task<IReadOnlyList<MapTileRef>> MapTilesAsync(string world, int zoom, int regionX, int regionZ, CancellationToken cancellationToken = default)
    {
        var path = "minecraft/map/worlds/" + Id(world) + "/tiles?zoom=" + Math.Clamp(zoom, 0, MinecraftMapGeometry.MaxZoomLevels).ToString(CultureInfo.InvariantCulture)
            + "&region=" + regionX.ToString(CultureInfo.InvariantCulture) + "," + regionZ.ToString(CultureInfo.InvariantCulture);
        return ParseTiles((await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false))?["data"]);
    }

    /// <summary>Live-Spieler eines Servers (Netzwerk-ID oder Slug); alle 5 s abfragen.</summary>
    public async Task<MapLive> MapLiveAsync(string server, CancellationToken cancellationToken = default)
    {
        if (!MinecraftMapGeometry.IsValidName(server))
        {
            throw new ArgumentException("Invalid server.", nameof(server));
        }

        return ParseLive((await SendAsync(HttpMethod.Get, "minecraft/map/live?server=" + Uri.EscapeDataString(server), null, cancellationToken).ConfigureAwait(false))?["data"], server);
    }

    /// <summary>Live-Spieler: <c>off</c>, <c>auth</c> (nur Eingeloggte) oder <c>public</c>; Antwort = gültige Einstellung.</summary>
    public async Task<string> SetMapLiveModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        if (!LiveModes.Contains(mode))
        {
            throw new ArgumentException("Invalid mode.", nameof(mode));
        }

        var data = (await SendAsync(new HttpMethod("PATCH"), "minecraft/map/settings", new JsonObject { ["live_players"] = mode }, cancellationToken).ConfigureAwait(false))?["data"];
        return Str(data, "live_players_mode") is { } m && LiveModes.Contains(m) ? m : mode;
    }

    /// <summary>Server auf der Karte aus-/einblenden; <paramref name="clearTiles"/> löscht beim Ausblenden alle Kacheln.</summary>
    public async Task<bool> SetMapServerHiddenAsync(string server, bool hidden, bool clearTiles = false, CancellationToken cancellationToken = default)
    {
        if (!MinecraftMapGeometry.IsValidName(server))
        {
            throw new ArgumentException("Invalid server.", nameof(server));
        }

        var body = new JsonObject { ["hidden"] = hidden };
        if (hidden && clearTiles)
        {
            body["clear_tiles"] = true;
        }

        var data = (await SendAsync(new HttpMethod("PATCH"), "minecraft/map/servers/" + Uri.EscapeDataString(server), body, cancellationToken).ConfigureAwait(false))?["data"];
        return data is JsonObject o && o["hidden"] is JsonValue ? Bool(o, "hidden") : hidden;
    }

    /// <summary>
    /// Kartenwelt ändern – nur gesetzte Felder; <paramref name="names"/> je Sprache (leer = Weltname, höchstens 64 Zeichen);
    /// <paramref name="clearTiles"/> nur zusammen mit Ausblenden.
    /// </summary>
    public async Task<MapWorld?> UpdateMapWorldAsync(string world, bool? hidden = null, IReadOnlyDictionary<string, string?>? names = null, bool clearTiles = false, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        if (hidden is { } h)
        {
            body["hidden"] = h;
        }

        if (hidden == true && clearTiles)
        {
            body["clear_tiles"] = true;
        }

        if (names is not null)
        {
            var obj = new JsonObject();
            foreach (var locale in HintLocales)
            {
                if (names.TryGetValue(locale, out var name))
                {
                    obj[locale] = Limit(name, MapNameMaxLength) ?? string.Empty;
                }
            }

            body["names"] = obj;
        }

        if (body.Count == 0)
        {
            return null;
        }

        return ParseWorld((await SendAsync(new HttpMethod("PATCH"), "minecraft/map/worlds/" + Id(world), body, cancellationToken).ConfigureAwait(false))?["data"]);
    }

    /// <summary>Alle Kacheln einer Welt löschen (das Plugin lädt sie nach <c>/map render</c> neu).</summary>
    public async Task ClearMapTilesAsync(string world, CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Delete, "minecraft/map/worlds/" + Id(world) + "/tiles", null, cancellationToken).ConfigureAwait(false);

    internal static MapOverview ParseMap(JsonNode? n)
    {
        var servers = n?["servers"] is JsonArray arr ? arr.Select(ParseServer).OfType<MapServer>().ToList() : [];
        var mode = Str(n, "live_players_mode") is { } m && LiveModes.Contains(m) ? m : (Bool(n, "live_players") ? LivePublic : LiveOff);
        var q = n?["quota"];
        var quota = q is JsonObject
            ? new MapQuota(Math.Max(0, LongOf(q, "used")), Math.Max(0, LongOf(q, "total")), Math.Max(0, LongOf(q, "warn_at")), Math.Max(0, LongOf(q, "per_server")), Bool(q, "warned"))
            : null;
        return new MapOverview(Bool(n, "live_players"), mode, servers, quota);
    }

    private static MapServer? ParseServer(JsonNode? n)
    {
        if (Str(n, "id") is not { } id)
        {
            return null;
        }

        var worlds = n?["worlds"] is JsonArray arr ? arr.Select(ParseWorld).OfType<MapWorld>().ToList() : [];
        return new MapServer(
            id,
            Str(n, "network_id"),
            Str(n, "name") ?? id,
            worlds,
            Bool(n, "hidden"),
            n?["public"] is JsonValue ? Bool(n, "public") : null,
            n?["bytes"] is JsonValue ? Math.Max(0, LongOf(n, "bytes")) : null);
    }

    internal static MapWorld? ParseWorld(JsonNode? n)
    {
        if (Str(n, "id") is not { } id || Str(n, "world") is not { } world)
        {
            return null;
        }

        var tiles = n?["tiles"];
        var markers = n?["markers"] is JsonArray arr
            ? arr.Select(m => Str(m, "id") is { } mid
                ? new MapMarker(Str(m, "type") ?? "warp", mid, Str(m, "label") ?? mid, IntOf(m, "x"), IntOf(m, "z"))
                : null).OfType<MapMarker>().ToList()
            : [];
        Dictionary<string, string>? names = null;
        if (n?["names"] is JsonObject no)
        {
            names = HintLocales.ToDictionary(l => l, l => Str(no, l) ?? string.Empty);
        }

        var regionSize = IntOf(tiles, "region_size");
        return new MapWorld(
            id,
            world,
            Str(n, "name") ?? world,
            Str(n, "environment") ?? "normal",
            Bool(n, "loaded"),
            IntOf(n?["spawn"], "x"),
            IntOf(n?["spawn"], "z"),
            Math.Clamp(IntOf(n, "zoom_levels"), 0, MinecraftMapGeometry.MaxZoomLevels),
            Math.Max(0, IntOf(tiles, "count")),
            regionSize > 0 ? regionSize : MinecraftMapGeometry.DefaultRegionSize,
            Str(tiles, "url"),
            Time(tiles, "updated_at"),
            markers,
            Time(n, "reported_at"),
            Bool(n, "hidden"),
            names,
            n?["bytes"] is JsonValue ? Math.Max(0, LongOf(n, "bytes")) : null);
    }

    internal static IReadOnlyList<MapTileRef> ParseTiles(JsonNode? n) =>
        n?["tiles"] is JsonArray arr
            ? arr.Select(t => t is JsonArray a && a.Count >= 3 && Whole(a[0]) is { } x && Whole(a[1]) is { } z
                    && a[2] is JsonValue v && v.GetValueKind() == JsonValueKind.String
                ? new MapTileRef(x, z, v.GetValue<string>())
                : (MapTileRef?)null).OfType<MapTileRef>().ToList()
            : [];

    internal static MapLive ParseLive(JsonNode? n, string server)
    {
        var players = n?["players"] is JsonArray arr
            ? arr.Select(p => Str(p, "uuid") is { } uuid && Str(p, "name") is { } name
                ? new MapLivePlayer(uuid, name, Str(p, "world") ?? string.Empty, Str(p, "world_id"), DoubleOf(p, "x") ?? 0, DoubleOf(p, "z") ?? 0, DoubleOf(p, "yaw"), Bool(p, "bedrock"), Str(p, "head_url"))
                : null).OfType<MapLivePlayer>().ToList()
            : [];
        var enabled = Bool(n, "enabled");
        return new MapLive(Str(n, "server") ?? server, enabled, Time(n, "updated_at"), enabled ? players : []);
    }

    private static int? Whole(JsonNode? n) =>
        n is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<long>(out var l) && l is >= int.MinValue and <= int.MaxValue ? (int)l : null;
}
