using System.Globalization;
using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Map;

public sealed record MapMember(string Slug, string Name, string? AvatarUrl, IReadOnlyList<string> Squads);

/// <summary>Ort auf der Karte (nur Stadt-Genauigkeit) mit den Mitgliedern dort.</summary>
public sealed record MapPin(double Lat, double Lng, string City, string? Country, IReadOnlyList<MapMember> Members);

/// <summary>Signierte Kachel-Vorlage vom eigenen Server (2 Stunden gültig, nie an fremde Kachel-Server).</summary>
public sealed record MapTiles(string UrlTemplate, DateTimeOffset? ExpiresAt, int MinZoom, int MaxZoom, int TileSize, string Attribution, Uri? AttributionUrl);

public sealed record MemberMap(IReadOnlyList<MapPin> Pins, MapTiles? Tiles);

/// <summary>Mitgliederkarte (Guide 36): Orte der Mitglieder mit Opt-in, Kacheln nur über die Plattform.</summary>
public sealed class MemberMapService(IRoGApiClient client)
{
    public async Task<MemberMap> LoadAsync(string? squad = null, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_map_indexAsync(squad, cancellationToken).ConfigureAwait(false);
        var pins = response.Data.Select(p => new MapPin(
            p.Lat,
            p.Lng,
            p.City,
            ApiValues.Text(p.Country),
            p.Members.Select(m => new MapMember(m.Slug, m.Name, ApiValues.Text(m.Avatar_url), m.Squads?.ToList() ?? [])).ToList())).ToList();

        var t = response.Meta?.Tiles;
        MapTiles? tiles = t is { Url: { Length: > 0 } url } && Uri.TryCreate(url.Replace("{z}", "0", StringComparison.Ordinal).Replace("{x}", "0", StringComparison.Ordinal).Replace("{y}", "0", StringComparison.Ordinal), UriKind.Absolute, out var check) && check.Scheme == Uri.UriSchemeHttps
            ? new MapTiles(url, ApiValues.Time(t.Expires_at), t.Min_zoom, t.Max_zoom, t.Tile_size > 0 ? t.Tile_size : 256, t.Attribution,
                Uri.TryCreate(t.Attribution_url, UriKind.Absolute, out var a) ? a : null)
            : null;
        return new MemberMap(pins, tiles);
    }
}

/// <summary>Web-Mercator-Rechnung für eine einfache Kachelkarte (Pixel bei Zoomstufe z, Ursprung oben links).</summary>
public static class WebMercator
{
    private const double MaxLatitude = 85.05112878;

    public static double WorldSize(int zoom, int tileSize = 256) => tileSize * Math.Pow(2, zoom);

    public static (double X, double Y) ToPixel(double lat, double lng, int zoom, int tileSize = 256)
    {
        var size = WorldSize(zoom, tileSize);
        var clamped = Math.Clamp(lat, -MaxLatitude, MaxLatitude) * Math.PI / 180;
        var x = (lng + 180) / 360 * size;
        var y = (1 - Math.Log(Math.Tan(clamped) + 1 / Math.Cos(clamped)) / Math.PI) / 2 * size;
        return (x, y);
    }

    public static (double Lat, double Lng) FromPixel(double x, double y, int zoom, int tileSize = 256)
    {
        var size = WorldSize(zoom, tileSize);
        var lng = x / size * 360 - 180;
        var n = Math.PI - 2 * Math.PI * y / size;
        var lat = 180 / Math.PI * Math.Atan(Math.Sinh(n));
        return (lat, lng);
    }

    /// <summary>
    /// Kacheln, die ein Ausschnitt (Mitte in Weltpixeln, Breite/Höhe) braucht. X wird um die Welt gewickelt
    /// (<c>X</c> = Kachel-Index für die URL, <c>Offset</c> = Spalte relativ zur Welt für die Position), Y begrenzt.
    /// </summary>
    public static IReadOnlyList<(int X, int Y, int Column)> VisibleTiles(double centerX, double centerY, double width, double height, int zoom, int tileSize = 256)
    {
        var count = 1 << zoom;
        var left = (int)Math.Floor((centerX - width / 2) / tileSize);
        var right = (int)Math.Floor((centerX + width / 2) / tileSize);
        var top = Math.Max(0, (int)Math.Floor((centerY - height / 2) / tileSize));
        var bottom = Math.Min(count - 1, (int)Math.Floor((centerY + height / 2) / tileSize));
        var tiles = new List<(int, int, int)>();
        for (var y = top; y <= bottom; y++)
        {
            for (var column = left; column <= right; column++)
            {
                tiles.Add((((column % count) + count) % count, y, column));
            }
        }

        return tiles;
    }

    public static Uri TileUrl(string template, int z, int x, int y) => new(template
        .Replace("{z}", z.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{x}", x.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{y}", y.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));

    /// <summary>Zoom und Mitte, damit alle Pins in den Ausschnitt passen (ohne Pins: Europa).</summary>
    public static (int Zoom, double CenterX, double CenterY) Fit(IEnumerable<(double Lat, double Lng)> points, double width, double height, int minZoom, int maxZoom, int tileSize = 256)
    {
        var list = points.ToList();
        if (list.Count == 0)
        {
            var z = Math.Clamp(4, minZoom, maxZoom);
            var (cx, cy) = ToPixel(50, 10, z, tileSize);
            return (z, cx, cy);
        }

        for (var zoom = Math.Min(maxZoom, 9); zoom > minZoom; zoom--)
        {
            var pixels = list.Select(p => ToPixel(p.Lat, p.Lng, zoom, tileSize)).ToList();
            var w = pixels.Max(p => p.X) - pixels.Min(p => p.X);
            var h = pixels.Max(p => p.Y) - pixels.Min(p => p.Y);
            if (w <= width * 0.8 && h <= height * 0.8)
            {
                return (zoom, (pixels.Max(p => p.X) + pixels.Min(p => p.X)) / 2, (pixels.Max(p => p.Y) + pixels.Min(p => p.Y)) / 2);
            }
        }

        var all = list.Select(p => ToPixel(p.Lat, p.Lng, minZoom, tileSize)).ToList();
        return (minZoom, (all.Max(p => p.X) + all.Min(p => p.X)) / 2, (all.Max(p => p.Y) + all.Min(p => p.Y)) / 2);
    }
}
