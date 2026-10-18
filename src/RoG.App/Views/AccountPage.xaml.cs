using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;
using RoG.Core.Features.Account;
using RoG.Core.Localization;

namespace RoG.App.Views;

public sealed partial class AccountPage : Page
{
    public AccountPage()
    {
        ViewModel = new AccountViewModel(App.Services);
        Editor = new ProfileEditViewModel(App.Services);
        Absences = new AbsencesViewModel(App.Services);
        Applications = new MyApplicationsViewModel(App.Services);
        Data = new AccountDataViewModel(App.Services);
        InitializeComponent();
    }

    public AccountViewModel ViewModel { get; }

    public ProfileEditViewModel Editor { get; }

    public AbsencesViewModel Absences { get; }

    public MyApplicationsViewModel Applications { get; }

    public AccountDataViewModel Data { get; }

    private void OnDataExpanding(Expander sender, ExpanderExpandingEventArgs args) => _ = Data.LoadAsync(silent: true);

    /// <summary>
    /// Konto löschen: zweistufig, mit Passwort bzw. Anzeigename als Bestätigung (Store-Pflicht: Löschen in der App).
    /// Aktive Mitglieder treten im selben Dialog zuerst aus dem Clan aus (Zustände: <see cref="AccountDeletionFlow"/>).
    /// </summary>
    private async void OnDeleteAccountClick(object sender, RoutedEventArgs e)
    {
        var l = App.Services.Localizer;
        var flow = AccountDeletionFlow.Start;
        var confirmation = new TextBox { Header = l.GetString(StringKeys.AccountDeleteConfirmHeader) };
        var hint = new InfoBar { IsClosable = false };
        var leave = new Button { Content = l.GetString(StringKeys.AccountDeleteLeaveButton), Visibility = Visibility.Collapsed, Style = (Style)Application.Current.Resources["RoGSecondaryButtonStyle"] };
        AutomationProperties.SetName(leave, l.GetString(StringKeys.AccountDeleteLeaveButton));
        var panel = new StackPanel { Spacing = 12, MaxWidth = 440 };
        panel.Children.Add(new TextBlock { Text = l.GetString(StringKeys.AccountDeleteWarning), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(confirmation);
        panel.Children.Add(hint);
        panel.Children.Add(leave);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = l.GetString(StringKeys.AccountDeleteTitle),
            Content = panel,
            PrimaryButtonText = l.GetString(StringKeys.AccountDeleteConfirm),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Close,
        };
        var leaveArmed = false;

        void Render()
        {
            var (message, severity) = Data.DeletionHint(flow);
            hint.Message = message;
            hint.Severity = severity;
            hint.IsOpen = message is not null;
            leave.Visibility = flow.CanLeave ? Visibility.Visible : Visibility.Collapsed;
            leave.Content = l.GetString(leaveArmed ? StringKeys.AccountDeleteLeaveConfirm : StringKeys.AccountDeleteLeaveButton);
            dialog.IsPrimaryButtonEnabled = flow.CanDelete;
        }

        // Austritt ist endgültig wie das Löschen: erster Klick fragt nach, zweiter tritt aus
        leave.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(confirmation.Text))
            {
                confirmation.Focus(FocusState.Programmatic);
                return;
            }

            if (!leaveArmed)
            {
                leaveArmed = true;
                Render();
                return;
            }

            leave.IsEnabled = false;
            try
            {
                flow = await Data.LeaveClanAsync(flow, confirmation.Text);
                leaveArmed = false;
                Render();
            }
            finally
            {
                leave.IsEnabled = true;
            }
        };

        dialog.PrimaryButtonClick += async (d, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                if (string.IsNullOrWhiteSpace(confirmation.Text))
                {
                    args.Cancel = true;
                    return;
                }

                if (await Data.DeleteAccountAsync(flow, confirmation.Text) is { } next)
                {
                    args.Cancel = true;
                    flow = next;
                    leaveArmed = false;
                    Render();
                }
            }
            finally
            {
                deferral.Complete();
            }
        };
        Render();
        await dialog.ShowAsync();
    }

    private void OnApplicationsExpanding(Expander sender, ExpanderExpandingEventArgs args) => _ = Applications.LoadAsync(silent: true);

    private void OnAbsencesExpanding(Expander sender, ExpanderExpandingEventArgs args) => _ = Absences.LoadAsync(silent: true);

    private void OnEditExpanding(Expander sender, ExpanderExpandingEventArgs args) => _ = Editor.LoadAsync(silent: true);

    private async void OnAvatarClick(object sender, RoutedEventArgs e) => await PickImageAsync(banner: false);

    private async void OnBannerClick(object sender, RoutedEventArgs e) => await PickImageAsync(banner: true);

    private async Task PickImageAsync(bool banner)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker { ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail };
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" })
        {
            picker.FileTypeFilter.Add(ext);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        if (await picker.PickSingleFileAsync() is { } file)
        {
            await Editor.SetImageAsync(file, banner);
            await ViewModel.LoadAsync();
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private async void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        var localizer = App.Services.Localizer;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = localizer.GetString(StringKeys.AccountSignOutTitle),
            Content = localizer.GetString(StringKeys.AccountSignOutText),
            PrimaryButtonText = localizer.GetString(StringKeys.AccountSignOutConfirm),
            CloseButtonText = localizer.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.SignOutAsync();
        }
    }
}
