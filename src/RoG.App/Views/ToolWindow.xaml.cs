using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using RoG.Core.Api;
using RoG.Core.Features.Tools;
using RoG.Core.Localization;

namespace RoG.App.Views;

/// <summary>
/// Ein Tool im eigenen Fenster (Guide 42 „Pflichten des App-Hosts“): Dokument als HTML-String (<c>about:blank</c>),
/// jede weitere Navigation, neue Fenster, Downloads, Berechtigungen und alle http(s)-Anfragen gesperrt, InPrivate
/// (keine Cookies/kein Speicher über das Fenster hinaus), Antworten der Bridge über <c>PostWebMessageAsJson</c>.
/// </summary>
public sealed partial class ToolWindow : Window, IToolBridgeHost, IDisposable
{
    // NavigateToString nimmt höchstens 2 MB
    private const int MaxDocumentChars = 1_900_000;

    private static readonly Dictionary<string, ToolWindow> s_open = new(StringComparer.Ordinal);

    private readonly string _slug;
    private readonly Uri? _webUrl;
    private readonly ToolService _tools = new(App.Services.Api.Http);
    private readonly DispatcherQueue _ui;
    private ToolBridge? _bridge;
    private bool _allowNavigation;

    private ToolWindow(string slug, string name, Uri? webUrl)
    {
        _slug = slug;
        _webUrl = webUrl;
        InitializeComponent();
        _ui = DispatcherQueue;
        TitleText.Text = name.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        Title = name;
        BrowserButton.Visibility = webUrl is null ? Visibility.Collapsed : Visibility.Visible;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 860));
        Closed += (_, _) =>
        {
            Dispose();
            Web.Close();
        };
        _ = LoadAsync();
    }

    public void Dispose()
    {
        _bridge?.Dispose();
        _bridge = null;
    }

    /// <summary>Tool öffnen; je Tool ein Fenster.</summary>
    public static void Open(string slug, string name, Uri? webUrl)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        if (!s_open.TryGetValue(slug, out var window))
        {
            window = new ToolWindow(slug, name, webUrl);
            s_open[slug] = window;
            window.Closed += (_, _) => s_open.Remove(slug);
        }

        window.Activate();
    }

    private async Task LoadAsync()
    {
        var l = App.Services.Localizer;
        Progress.Visibility = Visibility.Visible;
        Feedback.IsOpen = false;
        try
        {
            var document = await _tools.DocumentAsync(_slug);
            if (document.Html.Length > MaxDocumentChars)
            {
                Show(l.GetString(StringKeys.ToolsTooLarge), InfoBarSeverity.Warning);
                return;
            }

            await EnsureWebViewAsync();
            _bridge?.Dispose();
            _bridge = new ToolBridge(_tools, this, _slug);
            _allowNavigation = true;
            Web.CoreWebView2.NavigateToString(document.Html);
        }
        catch (ApiProblemException ex)
        {
            Show(l.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or System.Text.Json.JsonException or System.Runtime.InteropServices.COMException)
        {
            // COMException: WebView2-Laufzeit fehlt oder startet nicht
            Show(l.GetString(StringKeys.ToolsLoadFailed), InfoBarSeverity.Error);
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private async Task EnsureWebViewAsync()
    {
        if (Web.CoreWebView2 is not null)
        {
            return;
        }

        var environment = await CoreWebView2Environment.CreateAsync();
        var options = environment.CreateCoreWebView2ControllerOptions();
        options.IsInPrivateModeEnabled = true;
        await Web.EnsureCoreWebView2Async(environment, options);

        var core = Web.CoreWebView2!;
        var settings = core.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = true;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false; // alert/confirm wie im Sandbox-iframe gesperrt
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;

        core.NavigationStarting += (_, e) =>
        {
            // nur das eigene Dokument (NavigateToString) und about:
            if (_allowNavigation || e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                _allowNavigation = false;
                return;
            }

            e.Cancel = true;
        };
        core.FrameNavigationStarting += (_, e) => e.Cancel = !e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.ScriptDialogOpening += (_, _) => { }; // nicht bestätigen = abgelehnt

        // Netzwerk zusätzlich zur CSP sperren: jede Anfrage bekommt 403 (data:/blob: laufen nicht hierüber)
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) => e.Response = environment.CreateWebResourceResponse(null, 403, "Blocked", string.Empty);

        core.WebMessageReceived += OnWebMessageReceived;
    }

    private async void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_bridge is not { } bridge)
        {
            return;
        }

        string json;
        try
        {
            json = args.WebMessageAsJson;
        }
        catch (ArgumentException)
        {
            return;
        }

        var response = await bridge.HandleAsync(json);
        if (response is not null && ReferenceEquals(bridge, _bridge))
        {
            _ui.TryEnqueue(() =>
            {
                if (ReferenceEquals(bridge, _bridge))
                {
                    Web.CoreWebView2?.PostWebMessageAsJson(response);
                }
            });
        }
    }

    // --- IToolBridgeHost (aus der Bridge, ggf. nicht im UI-Thread) -------------------------------------------------

    public Task CopyAsync(string text) => OnUiAsync(() =>
    {
        App.Services.Clipboard.SetText(text);
        Show(Format(StringKeys.ToolsCopied, text.Length > 80 ? text[..80] + "…" : text), InfoBarSeverity.Success);
        return Task.FromResult(true);
    });

    public Task<bool> OpenLinkAsync(Uri url) => OnUiAsync(async () =>
    {
        var l = App.Services.Localizer;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = l.GetString(StringKeys.ToolsOpenLinkTitle),
            Content = new TextBlock { Text = Format(StringKeys.ToolsOpenLinkText, url.Host, url.AbsoluteUri), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            PrimaryButtonText = l.GetString(StringKeys.ToolsOpenLinkConfirm),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        await App.Services.Browser.OpenAsync(url);
        return true;
    });

    // Das Fenster scrollt selbst (WebView füllt es aus) – die gemeldete Höhe braucht es nicht
    public void Resize(int height)
    {
    }

    private Task<T> OnUiAsync<T>(Func<Task<T>> action)
    {
        var done = new TaskCompletionSource<T>();
        if (!_ui.TryEnqueue(async () =>
        {
            try
            {
                done.SetResult(await action());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        }))
        {
            done.SetException(new InvalidOperationException("Window closed."));
        }

        return done.Task;
    }

    private void Show(string message, InfoBarSeverity severity)
    {
        Feedback.Message = message;
        Feedback.Severity = severity;
        Feedback.IsOpen = true;
    }

    private static string Format(string key, params object[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, App.Services.Localizer.GetString(key), args);

    private void OnReloadClick(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private async void OnBrowserClick(object sender, RoutedEventArgs e)
    {
        if (_webUrl is not null)
        {
            await App.Services.Browser.OpenAsync(_webUrl);
        }
    }
}
