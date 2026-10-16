using Microsoft.Windows.ApplicationModel.Resources;
using RoG.Core.Auth;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using Windows.ApplicationModel.DataTransfer;

namespace RoG.App.Services;

/// <summary>Öffnet Adressen im Standardbrowser des Systems (App-Login nach RFC 8252: nie in einer WebView).</summary>
public sealed class LauncherBrowser : IBrowserLauncher
{
    public async Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken = default) =>
        await Windows.System.Launcher.LaunchUriAsync(uri);
}

/// <summary>Startet Spiele-Links (<c>steam://</c>, <c>et://</c> …) über das Programm, das Windows dafür registriert hat.</summary>
public sealed class WindowsExternalLauncher : IExternalLauncher
{
    public async Task<bool> CanLaunchAsync(Uri uri) =>
        await Windows.System.Launcher.QueryUriSupportAsync(uri, Windows.System.LaunchQuerySupportType.Uri) == Windows.System.LaunchQuerySupportStatus.Available;

    public async Task<bool> LaunchAsync(Uri uri) => await Windows.System.Launcher.LaunchUriAsync(uri);
}

/// <summary>Zwischenablage von Windows (nur aus dem UI-Thread aufrufen).</summary>
public sealed class WindowsClipboard : IClipboardService
{
    public void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}

/// <summary>Texte aus <c>Strings/&lt;Sprache&gt;/Resources.resw</c> (MRT Core des Windows App SDK).</summary>
public sealed class ResourceLocalizer : ILocalizer
{
    private readonly ResourceLoader _loader = new();

    public string GetString(string key)
    {
        var value = _loader.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }
}
