namespace RoG.Core.Features.Media;

/// <summary>
/// „Aus dem Internet“-Markierung (Mark-of-the-Web) für heruntergeladene Dateien: Windows/SmartScreen und Office prüfen
/// sie dann wie einen Browser-Download. Unter Windows als Datenstrom <c>Zone.Identifier</c> (Zone 3 = Internet),
/// auf anderen Systemen und Dateisystemen ohne Datenströme passiert nichts.
/// </summary>
public static class MarkOfTheWeb
{
    public const int InternetZone = 3;

    /// <summary>Inhalt des Datenstroms; die Quelle nur als http(s) ohne Zugangsdaten und Abfrage (signierte Links).</summary>
    public static string Content(Uri? source)
    {
        var text = "[ZoneTransfer]\r\nZoneId=" + InternetZone + "\r\n";
        if (source is { IsAbsoluteUri: true } && (source.Scheme == Uri.UriSchemeHttps || source.Scheme == Uri.UriSchemeHttp))
        {
            text += "HostUrl=" + source.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped) + "\r\n";
        }

        return text;
    }

    /// <summary>Markiert <paramref name="path"/>; Fehler (FAT32, Netzlaufwerk ohne Datenströme) werden ignoriert.</summary>
    public static bool TryApply(string path, Uri? source)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            File.WriteAllText(path + ":Zone.Identifier", Content(source));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
