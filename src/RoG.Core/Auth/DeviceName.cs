namespace RoG.Core.Auth;

/// <summary>
/// Gerätename für den App-Login (<c>device_name</c>, max. 80 Zeichen). Die Website zeigt ihn unter Konto → Apps &amp;
/// Geräte. Meldet sich dieselbe App mit demselben Namen erneut an, ersetzt der neue Token den alten.
/// </summary>
public static class DeviceName
{
    public const int MaxLength = 80;

    /// <summary>Präfix aller Windows-Geräte.</summary>
    public const string WindowsPrefix = "Windows-App";

    /// <summary>„Windows-App – PC von &lt;Name&gt;“, ohne Namen nur „Windows-App“.</summary>
    /// <param name="owner">Anzeigename (z. B. Windows-Benutzername); Steuerzeichen werden entfernt.</param>
    public static string ForWindows(string? owner)
    {
        var clean = new string((owner ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0)
        {
            return WindowsPrefix;
        }

        var name = $"{WindowsPrefix} – PC von {clean}";
        return name.Length <= MaxLength ? name : name[..MaxLength].TrimEnd();
    }
}
