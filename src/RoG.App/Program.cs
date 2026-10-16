using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace RoG.App;

/// <summary>
/// Eigener Einstieg statt des generierten <c>Main</c> (DISABLE_XAML_GENERATED_MAIN): nur eine Instanz der App.
/// Ein zweiter Start reicht seine Aktivierung (später auch <c>rog:</c>-Links, Sprungliste) an die laufende Instanz
/// weiter und beendet sich; die holt ihr Fenster nach vorn.
/// </summary>
public static class Program
{
    private const string InstanceKey = "rog-main";

    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (!IsMainInstance())
        {
            return 0;
        }

        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }

    private static bool IsMainInstance()
    {
        var main = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (main.IsCurrent)
        {
            main.Activated += (_, e) => App.OnRedirectedActivation(e);
            return true;
        }

        // Weiterleiten auf einem Hintergrund-Thread und hier warten: im STA-Thread würde das await blockieren
        // (Vorgehen aus dem Windows-App-SDK-Beispiel für Einzelinstanz-Apps).
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        using var done = new ManualResetEventSlim();
        _ = Task.Run(async () =>
        {
            try
            {
                await main.RedirectActivationToAsync(args);
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait(TimeSpan.FromSeconds(5));
        return false;
    }
}
