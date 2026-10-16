using Windows.Security.Credentials.UI;

namespace RoG.App.Services;

/// <summary>Windows Hello (PIN, Gesicht, Fingerabdruck) über <see cref="UserConsentVerifier"/> – für die App-Sperre.</summary>
public static class HelloService
{
    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            return await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Prüfung mit dem Hauptfenster als Besitzer (Desktop-Apps brauchen das Fenster-Handle).</summary>
    public static async Task<bool> VerifyAsync(string message)
    {
        try
        {
            var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(App.MainWindowHandle, message);
            return result == UserConsentVerificationResult.Verified;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
