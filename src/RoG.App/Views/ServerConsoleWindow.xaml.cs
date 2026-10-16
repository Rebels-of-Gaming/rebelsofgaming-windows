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
        Configs = new ServerConfigsViewModel(App.Services, slug, abilities, ViewModel.Show) { Confirm = ConfirmAsync };
        Backups = new ServerBackupsViewModel(App.Services, slug, ViewModel.Show) { Confirm = ConfirmAsync };
        InitializeComponent();
        ConsoleTab.Visibility = abilities.Console ? Visibility.Visible : Visibility.Collapsed;
        ConfigsTab.Visibility = abilities.FilesRead || abilities.FilesWrite ? Visibility.Visible : Visibility.Collapsed;
        BackupsTab.Visibility = abilities.Backups ? Visibility.Visible : Visibility.Collapsed;
        Tabs.SelectedItem = new[] { ConsoleTab, ConfigsTab, BackupsTab }.FirstOrDefault(t => t.Visibility == Visibility.Visible);
        Tabs.Visibility = Tabs.Items.Count(t => t.Visibility == Visibility.Visible) > 1 ? Visibility.Visible : Visibility.Collapsed;
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

    public ServerConfigsViewModel Configs { get; }

    public ServerBackupsViewModel Backups { get; }

    /// <summary>Reiter wechseln; Configs und Backups laden beim ersten Öffnen.</summary>
    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (ConsolePanel is null || ConfigsPanel is null || BackupsPanel is null)
        {
            return; // feuert schon in InitializeComponent
        }

        var selected = sender.SelectedItem;
        ConsolePanel.Visibility = selected == ConsoleTab ? Visibility.Visible : Visibility.Collapsed;
        ConfigsPanel.Visibility = selected == ConfigsTab ? Visibility.Visible : Visibility.Collapsed;
        BackupsPanel.Visibility = selected == BackupsTab ? Visibility.Visible : Visibility.Collapsed;
        if (selected == ConfigsTab)
        {
            _ = Configs.EnsureLoadedAsync();
        }
        else if (selected == BackupsTab)
        {
            _ = Backups.EnsureLoadedAsync();
        }
    }

    private async Task<bool> ConfirmAsync(string title, string text)
    {
        var l = App.Services.Localizer;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = l.GetString(StringKeys.CommonConfirm),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

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
        else if (e.Key is VirtualKey.Up or VirtualKey.Down && sender is TextBox box)
        {
            var text = e.Key == VirtualKey.Up ? ViewModel.History.Previous() : ViewModel.History.Next();
            if (text is not null)
            {
                e.Handled = true;
                ViewModel.Command = text;
                box.SelectionStart = text.Length;
            }
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
