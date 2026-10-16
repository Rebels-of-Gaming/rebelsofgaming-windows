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

    /// <summary>Dienste der laufenden App (Composition Root).</summary>
    public static AppServices Services { get; private set; } = null!;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Sprache muss vor dem ersten Laden von Ressourcen (x:Uid) feststehen.
        Services = await AppServices.CreateAsync();
        s_window = new MainWindow(Services);
        s_window.Activate();
    }

    /// <summary>Zweiter Start der App (Program.Main): vorhandenes Fenster nach vorn holen.</summary>
    internal static void OnRedirectedActivation(AppActivationArguments args)
    {
        var window = s_window;
        window?.DispatcherQueue.TryEnqueue(window.BringToFront);
    }
}
