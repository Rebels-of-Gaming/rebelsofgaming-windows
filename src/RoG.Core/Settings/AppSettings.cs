using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoG.Core.Config;

namespace RoG.Core.Settings;

/// <summary>
/// Lokale Einstellungen der App (keine personenbezogenen Daten, kein Token). Unter Windows liegen sie als JSON im
/// App-Datenordner des Pakets (<c>ApplicationData.Current.LocalFolder</c>) und verschwinden mit der Deinstallation.
/// </summary>
public sealed record AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary><c>system</c> (Windows-Anzeigesprache, sonst Deutsch), <c>de</c>, <c>en</c> oder <c>fr</c>.</summary>
    public string Language { get; init; } = LanguageResolver.System;

    /// <summary>Umgebung aus <c>Config/environments.json</c>, Standard Produktion.</summary>
    public string Environment { get; init; } = RoGEnvironment.Production;

    /// <summary>Mit Windows starten (StartupTask im MSIX-Manifest, abschaltbar in Windows selbst).</summary>
    public bool StartWithWindows { get; init; }

    /// <summary>Beim Schließen ins Infobereich-Symbol (Tray) minimieren statt beenden.</summary>
    public bool CloseToTray { get; init; } = true;

    /// <summary>Windows-Benachrichtigungen (Toasts) zeigen, solange die App läuft.</summary>
    public bool ToastNotifications { get; init; } = true;

    /// <summary>Gastmodus gewählt („Ohne Anmeldung umsehen“).</summary>
    public bool GuestMode { get; init; }

    /// <summary>Fenstergröße und -position beim letzten Schließen (physische Pixel); <c>null</c> = Standard.</summary>
    public WindowPlacement? Window { get; init; }

    /// <summary>Nach einem Upload den Direktlink in die Zwischenablage kopieren.</summary>
    public bool CopyLinkAfterUpload { get; init; } = true;

    /// <summary>Sichtbarkeit neuer Uploads: <c>public</c>, <c>members</c> oder <c>private</c>.</summary>
    public string UploadVisibility { get; init; } = "public";

    /// <summary>Zuletzt geöffneter Bereich (Navigations-Schlüssel wie <c>Nav_Servers</c>).</summary>
    public string? LastSection { get; init; }
}

/// <summary>Gemerkter Fensterzustand.</summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized)
{
    /// <summary>Mindestgröße laut Pflichtenheft (360 × 500 epx, hier großzügig in Pixeln).</summary>
    public const int MinWidth = 360;

    public const int MinHeight = 500;

    /// <summary>
    /// Passt die Lage an den sichtbaren Bildschirmbereich an (Monitor abgesteckt, Auflösung geändert): zu groß → verkleinern,
    /// außerhalb → zurück in den Bereich. <c>null</c>, wenn der gespeicherte Wert unbrauchbar ist.
    /// </summary>
    public WindowPlacement? FitInto(int areaX, int areaY, int areaWidth, int areaHeight)
    {
        if (Width < MinWidth || Height < MinHeight || areaWidth < MinWidth || areaHeight < MinHeight)
        {
            return null;
        }

        var w = Math.Min(Width, areaWidth);
        var h = Math.Min(Height, areaHeight);
        var x = Math.Clamp(X, areaX, areaX + areaWidth - w);
        var y = Math.Clamp(Y, areaY, areaY + areaHeight - h);
        return this with { X = x, Y = y, Width = w, Height = h };
    }
}

/// <summary>Speicher für <see cref="AppSettings"/>.</summary>
public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>JSON-Datei, atomar geschrieben (temporäre Datei + Umbenennen). Defekte oder fehlende Datei → Standardwerte.</summary>
public sealed class JsonFileSettingsStore(string path) : ISettingsStore
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Path { get; } = path;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(Path);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, s_json, cancellationToken).ConfigureAwait(false) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = Path + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, settings, s_json, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tmp, Path, overwrite: true);
    }
}

/// <summary>Bestimmt die App-Sprache: DE Standard, EN, FR.</summary>
public static class LanguageResolver
{
    public const string System = "system";

    public static readonly IReadOnlyList<string> Supported = ["de", "en", "fr"];

    /// <summary>Gewählte Sprache, bei <c>system</c> die Windows-Anzeigesprache, sonst Deutsch.</summary>
    public static string Resolve(string? setting, CultureInfo uiCulture)
    {
        ArgumentNullException.ThrowIfNull(uiCulture);
        if (setting is not null && Supported.Contains(setting))
        {
            return setting;
        }

        var lang = uiCulture.TwoLetterISOLanguageName;
        return Supported.Contains(lang) ? lang : "de";
    }

    /// <summary>Sprach-Tag für Windows-Ressourcen (<c>PrimaryLanguageOverride</c>).</summary>
    public static string ToResourceTag(string language) => language switch
    {
        "en" => "en-US",
        "fr" => "fr-FR",
        _ => "de-DE",
    };
}
