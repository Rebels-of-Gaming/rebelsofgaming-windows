using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.App.ViewModels;
using RoG.Core.Features.Members;
using RoG.Core.Features.Squads;
using RoG.Core.Localization;
using Windows.Storage.Pickers;

namespace RoG.App.Views;

/// <summary>Squad verwalten im eigenen Fenster; je Squad höchstens eins. Nach dem Schließen lädt die Squad-Seite neu.</summary>
public sealed partial class SquadAdminWindow : Window
{
    private static readonly Dictionary<string, SquadAdminWindow> s_open = new(StringComparer.Ordinal);
    private static readonly string[] s_imageTypes = [".png", ".jpg", ".jpeg", ".webp"];

    private SquadAdminWindow(SquadDetail squad, SquadAdminRights rights, Action<bool> closed)
    {
        ViewModel = new SquadAdminViewModel(App.Services, squad, rights) { Confirm = ConfirmAsync };
        InitializeComponent();
        ProfileTab.Visibility = rights.Edit ? Visibility.Visible : Visibility.Collapsed;
        MembersTab.Visibility = rights.Members ? Visibility.Visible : Visibility.Collapsed;
        ArchiveTab.Visibility = rights.Archive ? Visibility.Visible : Visibility.Collapsed;
        Tabs.SelectedItem = Tabs.Items.FirstOrDefault(t => t.Visibility == Visibility.Visible);
        Tabs.Visibility = Tabs.Items.Count(t => t.Visibility == Visibility.Visible) > 1 ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = squad.Summary.Name.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        Title = squad.Summary.Name;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(860, 680));
        Closed += (_, _) =>
        {
            s_open.Remove(squad.Summary.Slug);
            closed(ViewModel.Changed);
        };
    }

    public SquadAdminViewModel ViewModel { get; }

    public static void Open(SquadDetail squad, SquadAdminRights rights, Action<bool> closed)
    {
        ArgumentNullException.ThrowIfNull(squad);
        if (!s_open.TryGetValue(squad.Summary.Slug, out var window))
        {
            window = new SquadAdminWindow(squad, rights, closed);
            s_open[squad.Summary.Slug] = window;
        }

        window.Activate();
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (ProfilePanel is null || MembersPanel is null || ArchivePanel is null)
        {
            return; // feuert schon in InitializeComponent
        }

        var selected = sender.SelectedItem;
        ProfilePanel.Visibility = selected == ProfileTab ? Visibility.Visible : Visibility.Collapsed;
        MembersPanel.Visibility = selected == MembersTab ? Visibility.Visible : Visibility.Collapsed;
        ArchivePanel.Visibility = selected == ArchiveTab ? Visibility.Visible : Visibility.Collapsed;
        if (selected == MembersTab && ViewModel.Lineup.Count == 0)
        {
            _ = ViewModel.LoadMembersAsync();
        }
    }

    private async void OnMemberSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.NewMember = null;
            await ViewModel.SearchMembersAsync(sender.Text);
        }
    }

    private void OnMemberChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args) =>
        ViewModel.NewMember = args.SelectedItem as MemberSummary;

    private Task PickAsync(SquadImage kind) => PickImageAsync(async (stream, name, size) => await ViewModel.SetImageAsync(kind, stream, name, size));

    private async Task PickImageAsync(Func<Stream, string, long, Task> use)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail, SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (var ext in s_imageTypes)
        {
            picker.FileTypeFilter.Add(ext);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (await picker.PickSingleFileAsync() is { } file)
        {
            var props = await file.GetBasicPropertiesAsync();
            await using var stream = await file.OpenStreamForReadAsync();
            await use(stream, file.Name, (long)props.Size);
        }
    }

    private async void OnSetLogoClick(object sender, RoutedEventArgs e) => await PickAsync(SquadImage.Logo);

    private async void OnSetHeaderClick(object sender, RoutedEventArgs e) => await PickAsync(SquadImage.Header);

    private async void OnRemoveLogoClick(object sender, RoutedEventArgs e) => await ViewModel.RemoveImageAsync(SquadImage.Logo);

    private async void OnRemoveHeaderClick(object sender, RoutedEventArgs e) => await ViewModel.RemoveImageAsync(SquadImage.Header);

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
}
