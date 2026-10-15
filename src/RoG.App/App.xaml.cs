using Microsoft.UI.Xaml;
using RoG.App.Services;

namespace RoG.App;

/// <summary>Einstiegspunkt der WinUI-App.</summary>
public partial class App : Application
{
    private MainWindow? _window;

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
        _window = new MainWindow(Services);
        _window.Activate();
    }
}
