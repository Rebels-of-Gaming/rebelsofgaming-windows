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

    private ServerConsoleWindow(string slug, string serverId, string name, ServerAbilities abilities)
    {
        ViewModel = new ServerConsoleViewModel(App.Services, slug, name, abilities);
        Configs = new ServerConfigsViewModel(App.Services, slug, abilities, ViewModel.Show) { Confirm = ConfirmAsync };
        Backups = new ServerBackupsViewModel(App.Services, slug, ViewModel.Show) { Confirm = ConfirmAsync };
        Rcon = new ServerRconViewModel(App.Services, slug, ViewModel.Show);
        Bans = new ServerBansViewModel(App.Services, slug, ViewModel.Show) { Confirm = ConfirmAsync };
        Approvals = new ServerApprovalsViewModel(App.Services, slug, ViewModel.Show) { Confirm = ConfirmAsync };
        Files = new ServerFileBrowserViewModel(App.Services, slug, abilities, ViewModel.Show) { Confirm = ConfirmAsync, AskName = AskNameAsync };
        Whitelist = new ServerWhitelistViewModel(App.Services, slug, ViewModel.Show) { Confirm = ConfirmAsync };
        GameBans = new ServerGameBansViewModel(App.Services, slug, serverId, abilities, ViewModel.Show)
        {
            Confirm = ConfirmAsync,
            AskReason = AskReasonAsync,
            AskDuration = AskDurationAsync,
            ShowToken = ShowTokenAsync,
        };
        InitializeComponent();
        ConsoleTab.Visibility = abilities.Console ? Visibility.Visible : Visibility.Collapsed;
        ConfigsTab.Visibility = abilities.FilesRead || abilities.FilesWrite ? Visibility.Visible : Visibility.Collapsed;
        BackupsTab.Visibility = abilities.Backups ? Visibility.Visible : Visibility.Collapsed;
        RconTab.Visibility = abilities.Rcon ? Visibility.Visible : Visibility.Collapsed;
        BansTab.Visibility = abilities.Bans ? Visibility.Visible : Visibility.Collapsed;
        FilesTab.Visibility = abilities.FilesRead ? Visibility.Visible : Visibility.Collapsed;
        WhitelistTab.Visibility = abilities.Bans ? Visibility.Visible : Visibility.Collapsed;
        ApprovalsTab.Visibility = abilities.FilesApprove ? Visibility.Visible : Visibility.Collapsed;
        GameBansTab.Visibility = GameBans.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
        Tabs.SelectedItem = Tabs.Items.FirstOrDefault(t => t.Visibility == Visibility.Visible);
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

    public ServerRconViewModel Rcon { get; }

    public ServerBansViewModel Bans { get; }

    public ServerApprovalsViewModel Approvals { get; }

    public ServerFileBrowserViewModel Files { get; }

    public ServerWhitelistViewModel Whitelist { get; }

    public ServerGameBansViewModel GameBans { get; }

    /// <summary>Reiter wechseln; Configs und Backups laden beim ersten Öffnen.</summary>
    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (ConsolePanel is null || ConfigsPanel is null || BackupsPanel is null || RconPanel is null || BansPanel is null || ApprovalsPanel is null
            || FilesPanel is null || WhitelistPanel is null || GameBansPanel is null)
        {
            return; // feuert schon in InitializeComponent
        }

        var selected = sender.SelectedItem;
        ConsolePanel.Visibility = selected == ConsoleTab ? Visibility.Visible : Visibility.Collapsed;
        ConfigsPanel.Visibility = selected == ConfigsTab ? Visibility.Visible : Visibility.Collapsed;
        BackupsPanel.Visibility = selected == BackupsTab ? Visibility.Visible : Visibility.Collapsed;
        RconPanel.Visibility = selected == RconTab ? Visibility.Visible : Visibility.Collapsed;
        BansPanel.Visibility = selected == BansTab ? Visibility.Visible : Visibility.Collapsed;
        ApprovalsPanel.Visibility = selected == ApprovalsTab ? Visibility.Visible : Visibility.Collapsed;
        FilesPanel.Visibility = selected == FilesTab ? Visibility.Visible : Visibility.Collapsed;
        WhitelistPanel.Visibility = selected == WhitelistTab ? Visibility.Visible : Visibility.Collapsed;
        GameBansPanel.Visibility = selected == GameBansTab ? Visibility.Visible : Visibility.Collapsed;
        if (selected == ConfigsTab)
        {
            _ = Configs.EnsureLoadedAsync();
        }
        else if (selected == BackupsTab)
        {
            _ = Backups.EnsureLoadedAsync();
        }
        else if (selected == BansTab)
        {
            _ = Bans.EnsureLoadedAsync();
        }
        else if (selected == ApprovalsTab)
        {
            _ = Approvals.EnsureLoadedAsync();
        }
        else if (selected == FilesTab)
        {
            _ = Files.EnsureLoadedAsync();
        }
        else if (selected == WhitelistTab)
        {
            _ = Whitelist.EnsureLoadedAsync();
        }
        else if (selected == GameBansTab)
        {
            _ = GameBans.EnsureLoadedAsync();
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

    private async Task<string?> AskNameAsync(string title, string suggestion)
    {
        var l = App.Services.Localizer;
        var box = new TextBox { Text = suggestion, MinWidth = 420, IsSpellCheckEnabled = false };
        box.Loaded += (_, _) => box.Select(0, suggestion.Length);
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    box,
                    new TextBlock { Text = l.GetString(StringKeys.FilesBackupNameHint), TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["RoGMutedTextStyle"] },
                },
            },
            PrimaryButtonText = l.GetString(StringKeys.CommonConfirm),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;
    }

    /// <summary>Aufheben mit optionalem Grund; <c>null</c> = abgebrochen, sonst der Grund (ggf. leer).</summary>
    private async Task<string?> AskReasonAsync(string title, string text)
    {
        var l = App.Services.Localizer;
        var box = new TextBox { MinWidth = 420, MaxLength = 200, Header = l.GetString(StringKeys.GameBansLiftReason) };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, box } },
            PrimaryButtonText = l.GetString(StringKeys.CommonConfirm),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text.Trim() : null;
    }

    /// <summary>Neue Dauer ab jetzt (0 = dauerhaft) und optional neuer Grund für einen aktiven Ban.</summary>
    private async Task<(int Minutes, string? Reason)?> AskDurationAsync(GameBanItemViewModel item)
    {
        var l = App.Services.Localizer;
        var minutes = new NumberBox { Header = l.GetString(StringKeys.GameBansNewDuration), Minimum = 0, Value = 1440, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var reason = new TextBox { Header = l.GetString(StringKeys.GameBansNewReason), MaxLength = 200, Text = item.Ban.Reason ?? string.Empty, MinWidth = 420 };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = l.GetString(StringKeys.GameBansEditTitle),
            Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = item.Title, TextWrapping = TextWrapping.Wrap }, minutes, reason } },
            PrimaryButtonText = l.GetString(StringKeys.CommonConfirm),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var value = double.IsNaN(minutes.Value) ? 0 : (int)Math.Clamp(minutes.Value, 0, int.MaxValue);
        var text = reason.Text.Trim();
        return (value, text.Length == 0 || text == item.Ban.Reason ? null : text);
    }

    /// <summary>Neuen Plugin-Schlüssel einmalig zeigen; er bleibt nur in diesem Dialog und der Zwischenablage (auf Wunsch).</summary>
    private async Task ShowTokenAsync(string token)
    {
        var l = App.Services.Localizer;
        var box = new TextBox { Text = token, IsReadOnly = true, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), MinWidth = 460, IsSpellCheckEnabled = false };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, l.GetString(StringKeys.GameBansTokenTitle));
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = l.GetString(StringKeys.GameBansTokenTitle),
            Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = l.GetString(StringKeys.GameBansTokenText), TextWrapping = TextWrapping.Wrap }, box } },
            PrimaryButtonText = l.GetString(StringKeys.GameBansTokenCopy),
            CloseButtonText = l.GetString(StringKeys.GameBansTokenDone),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            App.Services.Clipboard.SetText(token);
            ViewModel.Show(l.GetString(StringKeys.GameBansTokenCopied), InfoBarSeverity.Success);
        }

        box.Text = string.Empty;
    }

    private void OnGameBanSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && GameBans.SearchCommand.CanExecute(null))
        {
            e.Handled = true;
            GameBans.SearchCommand.Execute(null);
        }
    }

    private void OnFileClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FileItemViewModel item)
        {
            _ = Files.ActivateAsync(item);
        }
    }

    private void OnCrumbClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            _ = Files.OpenAsync(path);
        }
    }

    private async void OnTemplateClick(object sender, RoutedEventArgs e)
    {
        if (await Configs.LoadTemplateAsync() is { } form)
        {
            await Controls.ConfigTemplateDialog.ShowAsync(
                Content.XamlRoot, form, App.Services.Localizer, Configs.ApplyTemplateAsync, () => ViewModel.FeedbackMessage ?? string.Empty);
        }
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
            window = new ServerConsoleWindow(server.Slug, server.Id, server.Name, abilities);
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

    private void OnRconKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && Rcon.SendCommand.CanExecute(null))
        {
            e.Handled = true;
            Rcon.SendCommand.Execute(null);
        }
        else if (e.Key is VirtualKey.Up or VirtualKey.Down && sender is TextBox box)
        {
            var text = e.Key == VirtualKey.Up ? Rcon.History.Previous() : Rcon.History.Next();
            if (text is not null)
            {
                e.Handled = true;
                Rcon.Command = text;
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
