namespace RoG.Core.Api;

/// <summary>
/// Plattform des Clients (<c>windows</c>, <c>linux</c> …) – ergibt User-Agent <c>RoG-&lt;Plattform&gt;/&lt;Version&gt;</c>
/// und <c>client=rog-&lt;plattform&gt;</c> für Reverb. Der Kern wird auch von der Linux-App genutzt; Standard ist Windows.
/// </summary>
public static class ClientPlatform
{
    public const string Windows = "windows";

    public const string Linux = "linux";

    /// <summary>Kleingeschrieben und nur <c>a–z0–9-</c>; leer oder ungültig → <see cref="Windows"/>.</summary>
    public static string Normalize(string? platform)
    {
        var value = (platform ?? string.Empty).Trim().ToLowerInvariant();
        return value.Length is > 0 and <= 32 && value.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
            ? value
            : Windows;
    }

    /// <summary>Produktname für den User-Agent: <c>windows</c> → <c>RoG-Windows</c>, <c>linux</c> → <c>RoG-Linux</c>.</summary>
    public static string Product(string? platform)
    {
        var value = Normalize(platform);
        return "RoG-" + char.ToUpperInvariant(value[0]) + value[1..];
    }

    /// <summary>Client-Kennung für Reverb: <c>rog-windows</c>, <c>rog-linux</c>.</summary>
    public static string ClientId(string? platform) => "rog-" + Normalize(platform);
}
