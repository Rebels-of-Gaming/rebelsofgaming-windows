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
        Applications = new ApplicationsAdminViewModel(App.Services, Moderation.Show) { AskDecision = AskDecisionAsync };
        InitializeComponent();
        Moderation.OpenMember = (user, report) => MemberModerationDialog.ShowAsync(XamlRoot, Moderation, user, report);
        Moderation.Confirm = ConfirmAsync;

        ApplicationsTab.Visibility = Visible(Applications.CanView);
        ReportsTab.Visibility = Visible(Moderation.CanReports);
        BansTab.Visibility = Visible(Moderation.CanReports);
        LogTab.Visibility = Visible(Moderation.CanLog);
        WordsTab.Visibility = Visible(Moderation.CanManage);
        var any = Moderation.Any || Applications.CanView;
        NoAccess.Visibility = Visible(!any);
        Tabs.Visibility = Visible(any);
        Tabs.SelectedItem = Tabs.Items.FirstOrDefault(t => t.Visibility == Visibility.Visible);
    }

    public ModerationViewModel Moderation { get; }

    public ApplicationsAdminViewModel Applications { get; }

    private void OnAppSearch(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => Applications.SearchNowCommand.Execute(null);

    /// <summary>Entscheidung bestätigen: optionale Nachricht an die bewerbende Person, Tage bei „verlängern“.</summary>
    private async Task<(bool Ok, string? Message, int? Days)> AskDecisionAsync(string title, bool needsDays)
    {
        var l = App.Services.Localizer;
        var message = new TextBox { Header = l.GetString(StringKeys.AppAdminDecisionMessage), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var days = new NumberBox { Header = l.GetString(StringKeys.ModDays), Value = 7, Minimum = 1, Maximum = 90, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var panel = new StackPanel { Spacing = 12, MinWidth = 380 };
        panel.Children.Add(message);
        if (needsDays)
        {
            panel.Children.Add(days);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = panel,
            PrimaryButtonText = l.GetString(StringKeys.CommonConfirm),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Close,
        };
        var ok = await dialog.ShowAsync() == ContentDialogResult.Primary;
        return (ok, message.Text, needsDays && !double.IsNaN(days.Value) ? (int)days.Value : null);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        OnTabChanged(Tabs, null);
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs? args)
    {
        if (ApplicationsPanel is null || ReportsPanel is null || BansPanel is null || LogPanel is null || WordsPanel is null)
        {
            return; // feuert schon in InitializeComponent
        }

        var selected = sender.SelectedItem;
        ApplicationsPanel.Visibility = Visible(selected == ApplicationsTab);
        ReportsPanel.Visibility = Visible(selected == ReportsTab);
        BansPanel.Visibility = Visible(selected == BansTab);
        LogPanel.Visibility = Visible(selected == LogTab);
        WordsPanel.Visibility = Visible(selected == WordsTab);
        if (selected == ApplicationsTab && Applications.Items.Count == 0)
        {
            _ = Applications.LoadAsync(reset: true);
        }
        else if (selected == ReportsTab && Moderation.Reports.Count == 0)
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
