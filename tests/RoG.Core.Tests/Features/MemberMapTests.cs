using RoG.Core.Features.Map;

namespace RoG.Core.Tests.Features;

public class MemberMapTests
{
    [Fact]
    public void Pixel_round_trip_and_known_points()
    {
        var (x, y) = WebMercator.ToPixel(0, 0, 1);
        Assert.Equal(256, x, 6);
        Assert.Equal(256, y, 6);

        var (lat, lng) = WebMercator.FromPixel(WebMercator.ToPixel(49.61, 6.13, 10).X, WebMercator.ToPixel(49.61, 6.13, 10).Y, 10);
        Assert.Equal(49.61, lat, 6);
        Assert.Equal(6.13, lng, 6);
    }

    [Fact]
    public void Visible_tiles_wrap_horizontally_and_clamp_vertically()
    {
        // Zoom 1: 2×2 Kacheln; Ausschnitt um die linke Weltkante herum
        var tiles = WebMercator.VisibleTiles(centerX: 0, centerY: 256, width: 256, height: 1000, zoom: 1);

        Assert.Contains((1, 0, -1), tiles); // gewickelt
        Assert.Contains((0, 1, 0), tiles);
        Assert.All(tiles, t => Assert.InRange(t.Y, 0, 1));
    }

    [Fact]
    public void Tile_url_fills_template()
    {
        var url = WebMercator.TileUrl("https://api.example/v1/map/tiles/{z}/{x}/{y}.png?sig=a", 5, 16, 10);
        Assert.Equal("https://api.example/v1/map/tiles/5/16/10.png?sig=a", url.ToString());
    }

    [Fact]
    public void Fit_keeps_all_points_in_view()
    {
        var points = new[] { (49.61, 6.13), (52.52, 13.40), (48.85, 2.35) };
        var (zoom, cx, cy) = WebMercator.Fit(points, 800, 600, 2, 12);

        Assert.InRange(zoom, 2, 9);
        foreach (var (lat, lng) in points)
        {
            var (px, py) = WebMercator.ToPixel(lat, lng, zoom);
            Assert.InRange(px - cx, -400, 400);
            Assert.InRange(py - cy, -300, 300);
        }
    }
}
