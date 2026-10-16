using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;
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
        InitializeComponent();
    }

    public AccountViewModel ViewModel { get; }

    public ProfileEditViewModel Editor { get; }

    public AbsencesViewModel Absences { get; }

    public MyApplicationsViewModel Applications { get; }

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
