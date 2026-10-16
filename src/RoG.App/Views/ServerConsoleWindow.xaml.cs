using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using RoG.App.ViewModels;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using Windows.System;

namespace RoG.App.Views;

/// <summary>Server-Steuerung im eigenen Fenster; je Server höchstens eins.</summary>
public sealed partial class ServerConsoleWindow : Window
{
    private static readonly Dictionary<string, ServerConsoleWindow> s_open = new(StringComparer.Ordinal);

    private ServerConsoleWindow(string slug, string name, ServerAbilities abilities)
    {
        ViewModel = new ServerConsoleViewModel(App.Services, slug, name, abilities);
        InitializeComponent();
        TitleText.Text = name.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        Title = name;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 640));
        ViewModel.LineAdded += (_, _) =>
        {
            if (ViewModel.Lines.Count > 0)
            {
                Output.ScrollIntoView(ViewModel.Lines[^1]);
            }
        };
        Closed += async (_, _) =>
        {
            s_open.Remove(slug);
            await ViewModel.DisposeAsync();
        };
        _ = ViewModel.StartAsync();
    }

    public ServerConsoleViewModel ViewModel { get; }

    public static void Open(GameServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (server.Control is not { } abilities)
        {
            return;
        }

        if (!s_open.TryGetValue(server.Slug, out var window))
        {
            window = new ServerConsoleWindow(server.Slug, server.Name, abilities);
            s_open[server.Slug] = window;
        }

        window.Activate();
    }

    private void OnCommandKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.SendCommand.CanExecute(null))
        {
            e.Handled = true;
            ViewModel.SendCommand.Execute(null);
        }
    }

    private async void OnKillClick(object sender, RoutedEventArgs e)
    {
        var l = App.Services.Localizer;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = l.GetString(StringKeys.ConsoleKillTitle),
            Content = l.GetString(StringKeys.ConsoleKillText),
            PrimaryButtonText = l.GetString(StringKeys.ConsoleKillConfirm),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.KillServerAsync();
        }
    }
}
