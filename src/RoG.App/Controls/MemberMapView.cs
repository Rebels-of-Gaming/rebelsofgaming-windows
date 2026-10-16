using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using RoG.Core.Features.Map;
using Windows.System;

namespace RoG.App.Controls;

/// <summary>
/// Schlanke Kachelkarte für die Mitgliederkarte: Kacheln nur über die signierte Vorlage der Plattform, Ziehen zum
/// Verschieben, Mausrad/+/−/Pfeiltasten, Pins je Ort mit Mitgliederliste. Rechnung in <see cref="WebMercator"/>.
/// </summary>
public sealed partial class MemberMapView : UserControl
{
    private readonly Canvas _tileLayer = new();
    private readonly Canvas _pinLayer = new();
    private readonly Dictionary<(int Column, int Y), Image> _tiles = [];
    private readonly HyperlinkButton _attribution = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8), FontSize = 12 };
    private MemberMap? _map;
    private int _zoom = 4;
    private double _centerX;
    private double _centerY;

    public MemberMapView()
    {
        var root = new Grid { Background = Brush("RoGBg2Brush") };
        root.Children.Add(_tileLayer);
        root.Children.Add(_pinLayer);

        var zoomButtons = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8), Spacing = 4 };
        zoomButtons.Children.Add(ZoomButton("+", 1));
        zoomButtons.Children.Add(ZoomButton("−", -1));
        root.Children.Add(zoomButtons);
        root.Children.Add(_attribution);
        _attribution.Click += (_, _) =>
        {
            if (_map?.Tiles?.AttributionUrl is { } url)
            {
                _ = App.Services.Browser.OpenAsync(url);
            }
        };

        Content = root;
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY;
        ManipulationDelta += (_, e) =>
        {
            _centerX -= e.Delta.Translation.X;
            _centerY -= e.Delta.Translation.Y;
            Render();
        };
        PointerWheelChanged += OnWheel;
        KeyDown += OnKey;
        SizeChanged += (_, _) =>
        {
            root.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, ActualWidth, ActualHeight) };
            Render();
        };
    }

    /// <summary>Mitglied in einem Pin angeklickt (Slug).</summary>
    public event EventHandler<string>? MemberInvoked;

    private int TileSize => _map?.Tiles?.TileSize ?? 256;

    private int MinZoom => _map?.Tiles?.MinZoom ?? 2;

    private int MaxZoom => _map?.Tiles?.MaxZoom ?? 12;

    /// <summary>Neue Daten; beim ersten Mal (oder <paramref name="fit"/>) auf alle Pins einpassen.</summary>
    public void SetMap(MemberMap map, bool fit)
    {
        var templateChanged = _map?.Tiles?.UrlTemplate != map.Tiles?.UrlTemplate;
        _map = map;
        _attribution.Content = map.Tiles?.Attribution ?? string.Empty;
        _attribution.Visibility = map.Tiles is null ? Visibility.Collapsed : Visibility.Visible;
        if (fit)
        {
            var width = ActualWidth > 0 ? ActualWidth : 900;
            var height = ActualHeight > 0 ? ActualHeight : 600;
            (_zoom, _centerX, _centerY) = WebMercator.Fit(map.Pins.Select(p => (p.Lat, p.Lng)), width, height, MinZoom, MaxZoom, TileSize);
        }

        if (templateChanged)
        {
            ClearTiles();
        }

        Render();
    }

    private Button ZoomButton(string text, int delta)
    {
        var button = new Button { Content = text, Width = 36, Height = 36, Padding = new Thickness(0), FontSize = 18 };
        AutomationProperties.SetName(button, App.Services.Localizer.GetString(delta > 0 ? RoG.Core.Localization.StringKeys.MapZoomIn : RoG.Core.Localization.StringKeys.MapZoomOut));
        button.Click += (_, _) => ZoomAt(delta, ActualWidth / 2, ActualHeight / 2);
        return button;
    }

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        ZoomAt(point.Properties.MouseWheelDelta > 0 ? 1 : -1, point.Position.X, point.Position.Y);
        e.Handled = true;
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        const double step = 80;
        switch (e.Key)
        {
            case VirtualKey.Left: _centerX -= step; break;
            case VirtualKey.Right: _centerX += step; break;
            case VirtualKey.Up: _centerY -= step; break;
            case VirtualKey.Down: _centerY += step; break;
            case VirtualKey.Add or (VirtualKey)187: ZoomAt(1, ActualWidth / 2, ActualHeight / 2); e.Handled = true; return;
            case VirtualKey.Subtract or (VirtualKey)189: ZoomAt(-1, ActualWidth / 2, ActualHeight / 2); e.Handled = true; return;
            default: return;
        }

        e.Handled = true;
        Render();
    }

    /// <summary>Zoomstufe ändern, der Punkt unter (<paramref name="x"/>, <paramref name="y"/>) bleibt stehen.</summary>
    private void ZoomAt(int delta, double x, double y)
    {
        var zoom = Math.Clamp(_zoom + delta, MinZoom, MaxZoom);
        if (zoom == _zoom)
        {
            return;
        }

        var factor = Math.Pow(2, zoom - _zoom);
        var worldX = _centerX + (x - ActualWidth / 2);
        var worldY = _centerY + (y - ActualHeight / 2);
        _centerX = worldX * factor - (x - ActualWidth / 2);
        _centerY = worldY * factor - (y - ActualHeight / 2);
        _zoom = zoom;
        ClearTiles();
        Render();
    }

    private void ClearTiles()
    {
        _tiles.Clear();
        _tileLayer.Children.Clear();
    }

    private void Render()
    {
        if (_map is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var size = TileSize;
        var world = WebMercator.WorldSize(_zoom, size);
        _centerY = Math.Clamp(_centerY, Math.Min(ActualHeight / 2, world / 2), Math.Max(world - ActualHeight / 2, world / 2));
        var originX = _centerX - ActualWidth / 2;
        var originY = _centerY - ActualHeight / 2;

        if (_map.Tiles is { } tiles)
        {
            var needed = WebMercator.VisibleTiles(_centerX, _centerY, ActualWidth, ActualHeight, _zoom, size);
            var keys = needed.Select(t => (t.Column, t.Y)).ToHashSet();
            foreach (var stale in _tiles.Keys.Where(k => !keys.Contains(k)).ToList())
            {
                _tileLayer.Children.Remove(_tiles[stale]);
                _tiles.Remove(stale);
            }

            foreach (var (x, y, column) in needed)
            {
                if (!_tiles.TryGetValue((column, y), out var image))
                {
                    image = new Image
                    {
                        Width = size,
                        Height = size,
                        Source = new BitmapImage(WebMercator.TileUrl(tiles.UrlTemplate, _zoom, x, y)),
                        IsHitTestVisible = false,
                    };
                    _tiles[(column, y)] = image;
                    _tileLayer.Children.Add(image);
                }

                Canvas.SetLeft(image, column * size - originX);
                Canvas.SetTop(image, y * size - originY);
            }
        }

        _pinLayer.Children.Clear();
        foreach (var pin in _map.Pins)
        {
            var (px, py) = WebMercator.ToPixel(pin.Lat, pin.Lng, _zoom, size);
            px += Math.Round((_centerX - px) / world) * world; // nächste Weltkopie
            var x = px - originX;
            var y = py - originY;
            if (x < -40 || y < -40 || x > ActualWidth + 40 || y > ActualHeight + 40)
            {
                continue;
            }

            var button = PinButton(pin);
            Canvas.SetLeft(button, x - 16);
            Canvas.SetTop(button, y - 16);
            _pinLayer.Children.Add(button);
        }
    }

    private Button PinButton(MapPin pin)
    {
        var grid = new Grid { Width = 32, Height = 32 };
        grid.Children.Add(new Ellipse { Fill = Brush("RoGRedBrush"), Stroke = Brush("RoGInkBrush"), StrokeThickness = 2 });
        grid.Children.Add(new TextBlock
        {
            Text = pin.Members.Count.ToString(System.Globalization.CultureInfo.CurrentCulture),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("RoGInkBrush"),
        });

        var list = new StackPanel { Spacing = 2, MaxWidth = 320 };
        list.Children.Add(new TextBlock
        {
            Text = pin.Country is null ? pin.City : $"{pin.City} ({pin.Country})",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        foreach (var member in pin.Members.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var link = new HyperlinkButton
            {
                Content = member.Squads.Count > 0 ? $"{member.Name} · {string.Join(", ", member.Squads)}" : member.Name,
                Padding = new Thickness(0, 2, 0, 2),
            };
            link.Click += (_, _) => MemberInvoked?.Invoke(this, member.Slug);
            list.Children.Add(link);
        }

        var button = new Button
        {
            Content = grid,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Flyout = new Flyout { Content = new ScrollViewer { Content = list, MaxHeight = 360 } },
        };
        AutomationProperties.SetName(button, $"{pin.City}: {pin.Members.Count}");
        ToolTipService.SetToolTip(button, pin.City);
        return button;
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
