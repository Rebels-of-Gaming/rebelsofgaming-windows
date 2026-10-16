using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Controls;
using RoG.Core.Api;
using RoG.Core.Features.Map;
using RoG.Core.Localization;
using RoG.Core.Navigation;

namespace RoG.App.Views;

/// <summary>Mitgliederkarte im eigenen Fenster (nur Mitglieder mit Opt-in, Stadt-Genauigkeit). Höchstens eins.</summary>
public sealed class MemberMapWindow : Window
{
    private static MemberMapWindow? s_open;

    private readonly MemberMapView _view = new();
    private readonly InfoBar _info = new() { IsClosable = false, Margin = new Thickness(16) };
    private readonly ProgressRing _loading = new() { IsActive = true, Width = 40, Height = 40 };
    private readonly MemberMapService _service = new(App.Services.Api.Client);
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(5) };
    private DateTimeOffset? _expiresAt;

    private MemberMapWindow()
    {
        var l = App.Services.Localizer;
        Title = l.GetString(StringKeys.MapTitle);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 700));

        var root = new Grid { Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["RoGBgBrush"] };
        root.Children.Add(_view);
        root.Children.Add(_loading);
        _info.VerticalAlignment = VerticalAlignment.Top;
        root.Children.Add(_info);
        Content = root;

        _view.MemberInvoked += (_, slug) =>
        {
            App.Services.Navigate(new AppRoute(AppSection.Members, slug));
            App.BringMainWindowToFront();
        };

        // Signierte Kachel-Vorlage gilt 2 Stunden – rechtzeitig neu holen
        _refresh.Tick += async (_, _) =>
        {
            if (_expiresAt is { } expires && expires - DateTimeOffset.UtcNow < TimeSpan.FromMinutes(10))
            {
                await LoadAsync(fit: false);
            }
        };
        _refresh.Start();
        Closed += (_, _) =>
        {
            _refresh.Stop();
            s_open = null;
        };
        _ = LoadAsync(fit: true);
    }

    public static void Open()
    {
        s_open ??= new MemberMapWindow();
        s_open.Activate();
    }

    private async Task LoadAsync(bool fit)
    {
        var l = App.Services.Localizer;
        try
        {
            var map = await _service.LoadAsync();
            _expiresAt = map.Tiles?.ExpiresAt;
            _view.SetMap(map, fit);
            _info.IsOpen = false;
            if (map.Pins.Count == 0)
            {
                ShowInfo(l.GetString(StringKeys.MapEmpty), InfoBarSeverity.Informational);
            }
            else if (map.Tiles is null)
            {
                ShowInfo(l.GetString(StringKeys.MapNoTiles), InfoBarSeverity.Warning);
            }
        }
        catch (ApiProblemException ex)
        {
            ShowInfo(l.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            ShowInfo(l.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
        finally
        {
            _loading.IsActive = false;
            _loading.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowInfo(string message, InfoBarSeverity severity)
    {
        _info.Message = message;
        _info.Severity = severity;
        _info.IsOpen = true;
    }
}
