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
        Overview = new AdminOverviewViewModel(App.Services, Moderation.Show);
        Members = new MembersAdminViewModel(App.Services, Moderation.Show)
        {
            CanModerate = Moderation.CanWarn || Moderation.CanBan,
            Moderate = user => MemberModerationDialog.ShowAsync(XamlRoot, Moderation, user, null),
        };
        Media = new MediaAdminViewModel(App.Services, Moderation.Show);
        Appeals = new BanAppealsViewModel(App.Services, Moderation.Show) { AskDecision = AskAppealDecisionAsync };
        InitializeComponent();
        Media.Confirm = ConfirmAsync;
        Moderation.OpenMember = (user, report) => MemberModerationDialog.ShowAsync(XamlRoot, Moderation, user, report);
        Moderation.Confirm = ConfirmAsync;

        OverviewTab.Visibility = Visible(Overview.Any);
        MembersTab.Visibility = Visible(Members.CanView);
        ApplicationsTab.Visibility = Visible(Applications.CanView);
        ReportsTab.Visibility = Visible(Moderation.CanReports);
        BansTab.Visibility = Visible(Moderation.CanReports);
        AppealsTab.Visibility = Visible(Appeals.CanView);
        LogTab.Visibility = Visible(Moderation.CanLog);
        WordsTab.Visibility = Visible(Moderation.CanManage);
        GalleryTab.Visibility = Visible(Media.CanGallery);
        UploadsTab.Visibility = Visible(Media.CanUploads);
        WebsiteTab.Visibility = Visible((Overview.Any && Media.HasWebLinks) || Media.HasNativeAreas);
        var any = Moderation.Any || Applications.CanView || Overview.Any || Members.CanView || Media.CanGallery || Media.CanUploads || Appeals.CanView;
        NoAccess.Visibility = Visible(!any);
        Tabs.Visibility = Visible(any);
        Tabs.SelectedItem = Tabs.Items.FirstOrDefault(t => t.Visibility == Visibility.Visible);
    }

    public ModerationViewModel Moderation { get; }

    public ApplicationsAdminViewModel Applications { get; }

    public AdminOverviewViewModel Overview { get; }

    public MembersAdminViewModel Members { get; }

    public MediaAdminViewModel Media { get; }

    public BanAppealsViewModel Appeals { get; }

    private void OnMemberSearch(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => Members.SearchNowCommand.Execute(null);

    private void OnAuditSearch(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => Overview.SearchAuditCommand.Execute(null);

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

    /// <summary>Einspruch entscheiden: öffentliche Begründung (bis 500 Zeichen), beim Ablehnen „sofort neuer Einspruch erlaubt“.</summary>
    private async Task<(string? Note, bool Reappeal)?> AskAppealDecisionAsync(string title, bool accept)
    {
        var l = App.Services.Localizer;
        var note = new TextBox { Header = l.GetString(StringKeys.BanAppealsNote), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = RoG.Core.Features.GameBans.PublicBanService.MaxNote, MinHeight = 80 };
        var reappeal = new CheckBox { Content = l.GetString(StringKeys.BanAppealsReappeal) };
        var panel = new StackPanel { Spacing = 12, MinWidth = 380 };
        panel.Children.Add(new TextBlock { Text = l.GetString(accept ? StringKeys.BanAppealsAcceptText : StringKeys.BanAppealsRejectText), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(note);
        if (!accept)
        {
            panel.Children.Add(reappeal);
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
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? (note.Text, !accept && reappeal.IsChecked == true) : null;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        OnTabChanged(Tabs, null);
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs? args)
    {
        if (OverviewPanel is null || MembersPanel is null || ApplicationsPanel is null || ReportsPanel is null || BansPanel is null || AppealsPanel is null || LogPanel is null || WordsPanel is null
            || GalleryPanel is null || UploadsPanel is null || WebsitePanel is null)
        {
            return; // feuert schon in InitializeComponent
        }

        var selected = sender.SelectedItem;
        OverviewPanel.Visibility = Visible(selected == OverviewTab);
        MembersPanel.Visibility = Visible(selected == MembersTab);
        ApplicationsPanel.Visibility = Visible(selected == ApplicationsTab);
        ReportsPanel.Visibility = Visible(selected == ReportsTab);
        BansPanel.Visibility = Visible(selected == BansTab);
        AppealsPanel.Visibility = Visible(selected == AppealsTab);
        LogPanel.Visibility = Visible(selected == LogTab);
        WordsPanel.Visibility = Visible(selected == WordsTab);
        GalleryPanel.Visibility = Visible(selected == GalleryTab);
        UploadsPanel.Visibility = Visible(selected == UploadsTab);
        WebsitePanel.Visibility = Visible(selected == WebsiteTab);
        if (selected == OverviewTab && Overview.Kpis.Count == 0 && Overview.Checks.Count == 0)
        {
            _ = Overview.LoadAsync();
        }
        else if (selected == MembersTab && Members.Items.Count == 0)
        {
            _ = Members.LoadAsync(reset: true);
        }
        else if (selected == ApplicationsTab && Applications.Items.Count == 0)
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
        else if (selected == AppealsTab)
        {
            _ = Appeals.EnsureLoadedAsync();
        }
        else if (selected == LogTab)
        {
            _ = Moderation.LoadLogAsync();
        }
        else if (selected == WordsTab)
        {
            _ = Moderation.LoadWordsAsync();
        }
        else if (selected == GalleryTab && Media.Pending.Count == 0)
        {
            _ = Media.LoadGalleryAsync();
        }
        else if (selected == UploadsTab && Media.Reports.Count == 0)
        {
            _ = Media.LoadUploadsAsync();
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
