using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.Controls;
using RoG.App.ViewModels;
using RoG.Core.Localization;

namespace RoG.App.Views;

/// <summary>Verwaltung: zuerst Moderation (Meldungen, Sperren, Mod-Log, Wortfilter); Reiter je Recht + Scope.</summary>
public sealed partial class AdminPage : Page
{
    public AdminPage()
    {
        Moderation = new ModerationViewModel(App.Services);
        InitializeComponent();
        Moderation.OpenMember = (user, report) => MemberModerationDialog.ShowAsync(XamlRoot, Moderation, user, report);
        Moderation.Confirm = ConfirmAsync;

        ReportsTab.Visibility = Visible(Moderation.CanReports);
        BansTab.Visibility = Visible(Moderation.CanReports);
        LogTab.Visibility = Visible(Moderation.CanLog);
        WordsTab.Visibility = Visible(Moderation.CanManage);
        NoAccess.Visibility = Visible(!Moderation.Any);
        Tabs.Visibility = Visible(Moderation.Any);
        Tabs.SelectedItem = Tabs.Items.FirstOrDefault(t => t.Visibility == Visibility.Visible);
    }

    public ModerationViewModel Moderation { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        OnTabChanged(Tabs, null);
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs? args)
    {
        if (ReportsPanel is null || BansPanel is null || LogPanel is null || WordsPanel is null)
        {
            return; // feuert schon in InitializeComponent
        }

        var selected = sender.SelectedItem;
        ReportsPanel.Visibility = Visible(selected == ReportsTab);
        BansPanel.Visibility = Visible(selected == BansTab);
        LogPanel.Visibility = Visible(selected == LogTab);
        WordsPanel.Visibility = Visible(selected == WordsTab);
        if (selected == ReportsTab && Moderation.Reports.Count == 0)
        {
            _ = Moderation.LoadReportsAsync(reset: true);
        }
        else if (selected == BansTab)
        {
            _ = Moderation.LoadBansAsync();
        }
        else if (selected == LogTab)
        {
            _ = Moderation.LoadLogAsync();
        }
        else if (selected == WordsTab)
        {
            _ = Moderation.LoadWordsAsync();
        }
    }

    private async Task<bool> ConfirmAsync(string title, string text)
    {
        var l = App.Services.Localizer;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = l.GetString(StringKeys.CommonConfirm),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
}
