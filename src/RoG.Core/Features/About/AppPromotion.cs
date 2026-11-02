using RoG.Core.Navigation;
using RoG.Core.Settings;

namespace RoG.Core.Features.About;

/// <summary>Store-Links aus <c>GET /v1</c> → <c>data.apps</c> (Clan-Einstellungen der Website). Nur https-Links.</summary>
public sealed record StoreLinks(Uri? AppStore, Uri? PlayStore, Uri? MicrosoftStore)
{
    public static StoreLinks None { get; } = new(null, null, null);

    public bool HasAny => AppStore is not null || PlayStore is not null || MicrosoftStore is not null;

    public static StoreLinks From(string? appStore, string? playStore, string? microsoftStore) =>
        new(Https(appStore), Https(playStore), Https(microsoftStore));

    /// <summary>
    /// Text für den Teilen-Dialog: Kopfzeile, dann je Store eine Zeile „Bezeichnung: Link“ – Windows zuerst. Ohne Links
    /// <c>null</c> (dann gibt es nichts zu teilen).
    /// </summary>
    public string? ShareText(string message, string microsoftLabel, string appStoreLabel, string playStoreLabel)
    {
        if (!HasAny)
        {
            return null;
        }

        var lines = new List<string> { message };
        if (MicrosoftStore is not null)
        {
            lines.Add($"{microsoftLabel}: {MicrosoftStore.AbsoluteUri}");
        }

        if (AppStore is not null)
        {
            lines.Add($"{appStoreLabel}: {AppStore.AbsoluteUri}");
        }

        if (PlayStore is not null)
        {
            lines.Add($"{playStoreLabel}: {PlayStore.AbsoluteUri}");
        }

        return string.Join('\n', lines);
    }

    private static Uri? Https(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;
}

/// <summary>Microsoft-Store-Eintrag der Windows-App (Partner Center, `docs/STORE.md`).</summary>
public static class StoreProduct
{
    public const string Id = "9PDWKHL6T188";

    /// <summary>Bewertungsseite in der Store-App – Ausweg, wenn die App nicht aus dem Store installiert ist.</summary>
    public static Uri ReviewUri { get; } = new($"ms-windows-store://review/?ProductId={Id}");
}

/// <summary>
/// Seltene Bitte um eine Store-Bewertung: nur bei Store-Installation, erst nach der Funktions-Tour, frühestens
/// <see cref="MinAge"/> nach dem ersten Start und nach <see cref="MomentsNeeded"/> positiven Momenten (Beitrag,
/// Nachricht, Zusage), höchstens alle <see cref="Interval"/>, nie nach „Nicht mehr fragen“. Keine Sterne-Vorgaben,
/// keine Anreize – nur die neutrale Bitte, der Store zeigt seinen eigenen Dialog.
/// </summary>
public static class ReviewPrompt
{
    public static readonly TimeSpan Interval = TimeSpan.FromDays(120);

    public static readonly TimeSpan MinAge = TimeSpan.FromDays(7);

    public const int MomentsNeeded = 3;

    public static bool ShouldAsk(AppSettings settings, DateTimeOffset now, bool isStoreInstall)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!isStoreInstall || settings.ReviewNeverAsk || settings.GuestMode || settings.TourVersion < FeatureTour.Version)
        {
            return false;
        }

        if (settings.FirstRunAt is not { } first || now - first < MinAge)
        {
            return false;
        }

        if (settings.ReviewAskedAt is { } asked && now - asked < Interval)
        {
            return false;
        }

        return settings.ReviewMoments >= MomentsNeeded;
    }

    /// <summary>Positiver Moment gezählt (gedeckelt, damit die Datei nicht wächst).</summary>
    public static AppSettings Moment(AppSettings settings) =>
        settings.ReviewMoments >= MomentsNeeded ? settings : settings with { ReviewMoments = settings.ReviewMoments + 1 };

    /// <summary>Gefragt (egal mit welcher Antwort): Zähler zurück, nächste Frage frühestens nach <see cref="Interval"/>.</summary>
    public static AppSettings Asked(AppSettings settings, DateTimeOffset now, bool never) =>
        settings with { ReviewAskedAt = now, ReviewMoments = 0, ReviewNeverAsk = settings.ReviewNeverAsk || never };
}

/// <summary>Eine Karte der Funktions-Tour: Schlüssel (für Texte <c>Tour_&lt;Key&gt;_Title/Text</c>), Symbol, Ziel.</summary>
public sealed record TourCard(string Key, string Glyph, AppSection Section);

/// <summary>
/// Kurze Funktions-Tour einmal nach der ersten Anmeldung (Überspringen jederzeit, später unter Einstellungen → Über die
/// App erneut). Eine neue <see cref="Version"/> zeigt die Tour nach größeren Änderungen noch einmal.
/// </summary>
public static class FeatureTour
{
    public const int Version = 1;

    public static IReadOnlyList<TourCard> Cards { get; } =
    [
        new("Forum", "", AppSection.Forum),
        new("Messages", "", AppSection.Messages),
        new("Events", "", AppSection.Calendar),
        new("Servers", "", AppSection.Servers),
        new("Notifications", "", AppSection.Notifications),
        new("Profile", "", AppSection.Account),
    ];

    /// <summary>Automatisch nur für Angemeldete (nicht als Gast), solange diese Tour-Version noch nicht gesehen ist.</summary>
    public static bool ShouldShow(AppSettings settings, bool signedIn)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return signedIn && !settings.GuestMode && settings.TourVersion < Version;
    }

    public static AppSettings Seen(AppSettings settings) => settings with { TourVersion = Version };
}
