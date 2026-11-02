using Microsoft.UI.Xaml.Media.Imaging;

namespace RoG.App.Services;

/// <summary>
/// Bilder aus API-Daten: nur von den Domains der Plattform (<see cref="RoG.Core.Config.RoGEnvironment.IsOwnMedia"/>),
/// fremde Hosts werden nicht geladen (kein Kontakt zu Dritten, Website-Regel). Ohne Bild → <c>null</c> (Platzhalter).
/// </summary>
public static class RemoteImage
{
    public static BitmapImage? From(Uri? uri, int decodePixelWidth = 0)
    {
        if (uri is null || !App.Services.Environment.IsOwnMedia(uri))
        {
            return null;
        }

        var image = new BitmapImage(uri);
        if (decodePixelWidth > 0)
        {
            image.DecodePixelWidth = decodePixelWidth;
        }

        return image;
    }
}
