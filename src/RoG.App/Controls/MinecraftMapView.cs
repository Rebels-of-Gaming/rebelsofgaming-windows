using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using RoG.Core.Features.Minecraft;
using RoG.Core.Localization;
using Windows.System;

namespace RoG.App.Controls;

/// <summary>
/// Weltkarte des Minecraft-Netzwerks (Guide 48): Kacheln 512 × 512 aus dem eigenen Speicher der Plattform, nur die,
/// die der Kachel-Index der sichtbaren Regionen nennt. Ziehen zum Verschieben, Mausrad/+/−/Pfeiltasten, Warps (gold),
/// Spawn und Live-Spieler (grün). Rechnung in <see cref="MapViewport"/>; Norden oben.
/// </summary>
public sealed partial class MinecraftMapView : UserControl
{
    private const double ZoomStep = 1.5;

    private readonly Canvas _tileLayer = new() { IsHitTestVisible = false };
    private readonly Canvas _overlay = new();
    private readonly TextBlock _coordinates = new() { Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Dictionary<(int Zoom, int X, int Z), Image> _images = [];
    private readonly Dictionary<(int Zoom, int X, int Z), Dictionary<(int X, int Z), string>> _regions = [];
    private readonly HashSet<(int Zoom, int X, int Z)> _loading = [];
    private MapViewport _view = new(0);
    private MapWorld? _world;
    private IReadOnlyList<MapLivePlayer> _players = [];
    private Windows.Foundation.Point? _drag;
    private bool _renderQueued;
    private int _generation;

    public MinecraftMapView()
    {
        var root = new Grid { Background = Brush("RoGBg2Brush") };
        root.Children.Add(_tileLayer);
        root.Children.Add(_overlay);

        var buttons = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8), Spacing = 4 };
        buttons.Children.Add(MapButton("+", StringKeys.MapZoomIn, () => ZoomAt(ZoomStep, _view.Width / 2, _view.Height / 2)));
        buttons.Children.Add(MapButton("−", StringKeys.MapZoomOut, () => ZoomAt(1 / ZoomStep, _view.Width / 2, _view.Height / 2)));
        buttons.Children.Add(MapButton("⌂", StringKeys.MinecraftMapSpawn, () =>
        {
            if (_world is { } w)
            {
                CenterOn(w.SpawnX, w.SpawnZ);
            }
        }));
        root.Children.Add(buttons);

        _coordinates.Style = (Style)Application.Current.Resources["RoGMutedTextStyle"];
        _coordinates.FontSize = 12;
        AutomationProperties.SetAccessibilityView(_coordinates, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        root.Children.Add(_coordinates);

        Content = root;
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        AutomationProperties.SetName(this, App.Services.Localizer.GetString(StringKeys.MinecraftMapViewName));
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += (_, e) => EndDrag(e);
        PointerCanceled += (_, e) => EndDrag(e);
        PointerCaptureLost += (_, _) => _drag = null;
        PointerWheelChanged += OnWheel;
        KeyDown += OnKey;
        SizeChanged += (_, _) =>
        {
            root.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, ActualWidth, ActualHeight) };
            _view.Resize(ActualWidth, ActualHeight);
            QueueRender();
        };
    }

    /// <summary>Lädt den Kachel-Index einer Region (Welt-ID, Stufe, rx, rz); Fehler → leere Liste.</summary>
    public Func<string, int, int, int, Task<IReadOnlyList<MapTileRef>>>? TileIndexLoader { get; set; }

    /// <summary>Neue Welt: Kacheln und Index verwerfen, auf <paramref name="x"/>/<paramref name="z"/> (sonst Spawn) zentrieren.</summary>
    public void SetWorld(MapWorld? world, int? x = null, int? z = null)
    {
        var same = _world is { } old && world is not null && old.Id == world.Id && old.TileUrl == world.TileUrl;
        _world = world;
        if (!same)
        {
            _generation++;
            _regions.Clear();
            _loading.Clear();
            ClearImages();
            _view = new MapViewport(world?.ZoomLevels ?? 0);
            _view.Resize(ActualWidth, ActualHeight);
            _view.SetScale(1);
            _view.CenterOn(x ?? world?.SpawnX ?? 0, z ?? world?.SpawnZ ?? 0);
        }
        else if (x is not null || z is not null)
        {
            _view.CenterOn(x ?? _view.CenterX, z ?? _view.CenterZ);
        }

        QueueRender();
    }

    /// <summary>Kachel-Index neu laden (z. B. nach „Aktualisieren“), ohne die Ansicht zu verschieben.</summary>
    public void ReloadTiles()
    {
        _generation++;
        _regions.Clear();
        _loading.Clear();
        QueueRender();
    }

    public void SetPlayers(IReadOnlyList<MapLivePlayer> players)
    {
        _players = players;
        QueueRender();
    }

    public void CenterOn(double x, double z)
    {
        _view.CenterOn(x, z);
        QueueRender();
    }

    private static Button MapButton(string text, string nameKey, Action click)
    {
        var button = new Button { Content = text, Width = 36, Height = 36, Padding = new Thickness(0), FontSize = 18 };
        var name = App.Services.Localizer.GetString(nameKey);
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInButton(source))
        {
            return; // Klick auf einen Zoom-Knopf
        }

        _drag = e.GetCurrentPoint(this).Position;
        CapturePointer(e.Pointer);
        Focus(FocusState.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this).Position;
        var (bx, bz) = _view.ScreenToBlock(point.X, point.Y);
        _coordinates.Text = string.Format(CultureInfo.CurrentCulture, "x {0:0} · z {1:0}", Math.Floor(bx), Math.Floor(bz));
        if (_drag is not { } last)
        {
            return;
        }

        _view.Pan(point.X - last.X, point.Y - last.Y);
        _drag = point;
        QueueRender();
        e.Handled = true;
    }

    private void EndDrag(PointerRoutedEventArgs e)
    {
        if (_drag is not null)
        {
            _drag = null;
            ReleasePointerCapture(e.Pointer);
        }
    }

    private bool IsInButton(DependencyObject source)
    {
        for (var d = source; d is not null && !ReferenceEquals(d, this); d = VisualTreeHelper.GetParent(d))
        {
            if (d is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var delta = point.Properties.MouseWheelDelta;
        ZoomAt(Math.Pow(ZoomStep, delta / 120.0), point.Position.X, point.Position.Y);
        e.Handled = true;
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        const double step = 80;
        switch (e.Key)
        {
            case VirtualKey.Left: _view.Pan(step, 0); break;
            case VirtualKey.Right: _view.Pan(-step, 0); break;
            case VirtualKey.Up: _view.Pan(0, step); break;
            case VirtualKey.Down: _view.Pan(0, -step); break;
            case VirtualKey.Add or (VirtualKey)187: ZoomAt(ZoomStep, _view.Width / 2, _view.Height / 2); e.Handled = true; return;
            case VirtualKey.Subtract or (VirtualKey)189: ZoomAt(1 / ZoomStep, _view.Width / 2, _view.Height / 2); e.Handled = true; return;
            case VirtualKey.Home when _world is { } w: _view.CenterOn(w.SpawnX, w.SpawnZ); break;
            default: return;
        }

        e.Handled = true;
        QueueRender();
    }

    private void ZoomAt(double factor, double x, double y)
    {
        _view.ZoomAt(factor, x, y);
        QueueRender();
    }

    private void ClearImages()
    {
        _images.Clear();
        _tileLayer.Children.Clear();
    }

    /// <summary>Höchstens ein Neuaufbau je Frame (Ziehen und Mausrad feuern oft).</summary>
    private void QueueRender()
    {
        if (_renderQueued)
        {
            return;
        }

        _renderQueued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            _renderQueued = false;
            Render();
        });
    }

    private void Render()
    {
        if (_world is not { } world || _view.Width <= 0 || _view.Height <= 0)
        {
            ClearImages();
            _overlay.Children.Clear();
            return;
        }

        var zoom = _view.TileZoom;
        var visible = _view.VisibleTiles();
        var wanted = new HashSet<(int Zoom, int X, int Z)>();
        foreach (var (x, z) in visible)
        {
            var region = (zoom, MinecraftMapGeometry.RegionIndex(x, world.RegionSize), MinecraftMapGeometry.RegionIndex(z, world.RegionSize));
            if (!_regions.TryGetValue(region, out var index))
            {
                LoadRegion(world, region);
                continue;
            }

            if (index.TryGetValue((x, z), out var version) && MinecraftMapGeometry.TileUri(world.TileUrl, zoom, x, z, version) is { } uri)
            {
                var key = (zoom, x, z);
                wanted.Add(key);
                if (!_images.TryGetValue(key, out var image))
                {
                    image = new Image { Source = new BitmapImage(uri), Stretch = Stretch.Fill, IsHitTestVisible = false };
                    _images[key] = image;
                    _tileLayer.Children.Add(image);
                }

                var (left, top, size) = _view.TileRect(zoom, x, z);
                image.Width = image.Height = size + 0.5; // halber Pixel gegen Haarlinien zwischen Kacheln
                Canvas.SetLeft(image, left);
                Canvas.SetTop(image, top);
            }
        }

        foreach (var stale in _images.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            _tileLayer.Children.Remove(_images[stale]);
            _images.Remove(stale);
        }

        RenderOverlay(world);
    }

    private async void LoadRegion(MapWorld world, (int Zoom, int X, int Z) region)
    {
        if (TileIndexLoader is null || !_loading.Add(region))
        {
            return;
        }

        var generation = _generation;
        IReadOnlyList<MapTileRef> tiles;
        try
        {
            tiles = await TileIndexLoader(world.Id, region.Zoom, region.X, region.Z);
        }
#pragma warning disable CA1031 // eine fehlende Region darf die Karte nicht kaputt machen – leer anzeigen
        catch (Exception)
#pragma warning restore CA1031
        {
            tiles = [];
        }

        if (generation != _generation)
        {
            return; // inzwischen andere Welt bzw. neu geladen
        }

        _loading.Remove(region);
        _regions[region] = tiles.GroupBy(t => (t.X, t.Z)).ToDictionary(g => g.Key, g => g.First().Version);
        QueueRender();
    }

    private void RenderOverlay(MapWorld world)
    {
        _overlay.Children.Clear();
        AddPoint(world.SpawnX, world.SpawnZ, Brush("RoGInkBrush"), App.Services.Localizer.GetString(StringKeys.MinecraftMapSpawn), showLabel: false);
        foreach (var marker in world.Markers)
        {
            AddPoint(marker.X, marker.Z, Brush("RoGGoldBrush"), marker.Label, showLabel: _view.PixelsPerBlock >= 0.25);
        }

        foreach (var player in _players.Where(p => p.WorldId == world.Id || (p.WorldId is null && p.World == world.World)))
        {
            AddPlayer(player);
        }
    }

    private bool OnScreen(double x, double y) => x >= -40 && y >= -40 && x <= _view.Width + 40 && y <= _view.Height + 40;

    private void AddPoint(double bx, double bz, Brush fill, string label, bool showLabel)
    {
        var (x, y) = _view.BlockToScreen(bx + 0.5, bz + 0.5);
        if (!OnScreen(x, y))
        {
            return;
        }

        var dot = new Rectangle { Width = 10, Height = 10, Fill = fill, Stroke = Brush("RoGBgBrush"), StrokeThickness = 1.5, RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5), RenderTransform = new RotateTransform { Angle = 45 } };
        ToolTipService.SetToolTip(dot, label);
        AutomationProperties.SetName(dot, label);
        Canvas.SetLeft(dot, x - 5);
        Canvas.SetTop(dot, y - 5);
        _overlay.Children.Add(dot);
        if (showLabel)
        {
            var text = new TextBlock { Text = label, FontSize = 12, Foreground = fill, IsHitTestVisible = false };
            Canvas.SetLeft(text, x + 8);
            Canvas.SetTop(text, y - 9);
            _overlay.Children.Add(text);
        }
    }

    private void AddPlayer(MapLivePlayer player)
    {
        var (x, y) = _view.BlockToScreen(player.X, player.Z);
        if (!OnScreen(x, y))
        {
            return;
        }

        var head = new Grid { Width = 22, Height = 22 };
        head.Children.Add(new Ellipse { Fill = Brush("RoGBg3Brush"), Stroke = Brush("RoGGreenBrush"), StrokeThickness = 2 });
        if (player.HeadUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            head.Children.Add(new Image { Source = new BitmapImage(uri) { DecodePixelWidth = 32 }, Width = 14, Height = 14 });
        }
        else
        {
            head.Children.Add(new TextBlock
            {
                Text = player.Name[..1].ToUpperInvariant(),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("RoGInkBrush"),
            });
        }

        var label = player.Bedrock ? player.Name + " · Bedrock" : player.Name;
        ToolTipService.SetToolTip(head, label);
        AutomationProperties.SetName(head, label);
        Canvas.SetLeft(head, x - 11);
        Canvas.SetTop(head, y - 11);
        _overlay.Children.Add(head);

        var text = new TextBlock { Text = player.Name, FontSize = 12, Foreground = Brush("RoGGreenBrush"), IsHitTestVisible = false };
        Canvas.SetLeft(text, x + 14);
        Canvas.SetTop(text, y - 9);
        _overlay.Children.Add(text);
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
