using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using RoG.App.Services;

namespace RoG.App;

/// <summary>Einstiegspunkt der WinUI-App.</summary>
public partial class App : Application
{
    private static MainWindow? s_window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // Keine Inhalte/Token ins Log – nur Typ und Meldung. Crash-Reporting folgt (offene Frage wie Mobile-App).
            System.Diagnostics.Debug.WriteLine($"Unbehandelt: {e.Exception.GetType().Name}: {e.Message}");
        };
    }

    /// <summary>Fensterhandle des Hauptfensters (Datei-Dialoge brauchen es in WinUI 3).</summary>
    public static nint MainWindowHandle => s_window is null ? 0 : WinRT.Interop.WindowNative.GetWindowHandle(s_window);

    /// <summary>XamlRoot des zuletzt aktiven Fensters (Dialoge aus ViewModels, z. B. „Melden“ im Chat-Fenster).</summary>
    public static XamlRoot? ActiveXamlRoot { get; set; }

    /// <summary>Hauptfenster nach vorn holen (z. B. aus einem Nebenfenster wie der Karte).</summary>
    public static void BringMainWindowToFront() => s_window?.BringToFront();

    /// <summary>Dienste der laufenden App (Composition Root).</summary>
    public static AppServices Services { get; private set; } = null!;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Toasts: Microsoft verlangt die Registrierung vor dem ersten Fenster
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        ToastService.Register();

        // Sprache muss vor dem ersten Laden von Ressourcen (x:Uid) feststehen.
        Services = await AppServices.CreateAsync();
        s_window = new MainWindow(Services);
        if (activation.Kind == ExtendedActivationKind.StartupTask && Services.Settings.CloseToTray)
        {
            // Autostart mit Windows: nur im Infobereich starten, Fenster erst auf Klick
            s_window.StartHidden();
        }
        else
        {
            s_window.Activate();
        }

        s_window.HandleActivation(activation);
    }

    /// <summary>Zweiter Start der App (Program.Main): vorhandenes Fenster nach vorn holen.</summary>
    internal static void OnRedirectedActivation(AppActivationArguments args)
    {
        var window = s_window;
        window?.DispatcherQueue.TryEnqueue(() =>
        {
            window.BringToFront();
            window.HandleActivation(args);
        });
    }
}
