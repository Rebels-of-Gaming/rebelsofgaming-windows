using Microsoft.Windows.ApplicationModel.Resources;
using RoG.Core.Auth;
using RoG.Core.Localization;

namespace RoG.App.Services;

/// <summary>Öffnet Adressen im Standardbrowser des Systems (App-Login nach RFC 8252: nie in einer WebView).</summary>
public sealed class LauncherBrowser : IBrowserLauncher
{
    public async Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken = default) =>
        await Windows.System.Launcher.LaunchUriAsync(uri);
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
