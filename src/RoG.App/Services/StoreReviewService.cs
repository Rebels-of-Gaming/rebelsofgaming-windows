using Windows.Services.Store;

namespace RoG.App.Services;

/// <summary>
/// „App bewerten“: bei Store-Installation der Bewertungsdialog des Stores (<see cref="StoreContext.RequestRateAndReviewAppAsync"/>,
/// braucht das Fenster-Handle), sonst die Bewertungsseite in der Store-App.
/// </summary>
public static class StoreReviewService
{
    public static bool IsStoreInstall { get; } = WindowsStoreUpdateSource.DetectStoreInstall();

    /// <summary><c>true</c>, wenn der Dialog bzw. die Store-Seite geöffnet wurde (Abbrechen zählt ebenfalls).</summary>
    public static async Task<bool> RequestAsync()
    {
        if (IsStoreInstall)
        {
            try
            {
                var context = StoreContext.GetDefault();
                WinRT.Interop.InitializeWithWindow.Initialize(context, App.MainWindowHandle);
                var result = await context.RequestRateAndReviewAppAsync();
                if (result.Status is StoreRateAndReviewStatus.Succeeded or StoreRateAndReviewStatus.CanceledByUser)
                {
                    return true;
                }
            }
#pragma warning disable CA1031 // Store-Dienst nicht verfügbar → Store-Seite als Ausweg
            catch (Exception)
#pragma warning restore CA1031
            {
                // weiter mit der Store-Seite
            }
        }

        return await Windows.System.Launcher.LaunchUriAsync(RoG.Core.Features.About.StoreProduct.ReviewUri);
    }
}
