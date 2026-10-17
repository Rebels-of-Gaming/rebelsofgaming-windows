using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;
using RoG.Core.Localization;
using Windows.Storage.Pickers;

namespace RoG.App.Views;

public sealed partial class WikiPage : Page
{
    private static readonly string[] s_imageTypes = [".png", ".jpg", ".jpeg", ".gif", ".webp"];
    private static readonly string[] s_formats = ["wikitext", "markdown"];

    public WikiPage()
    {
        ViewModel = new WikiViewModel(App.Services);
        InitializeComponent();
    }

    public WikiViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string { Length: > 0 } path)
        {
            await ViewModel.OpenPathAsync(path);
        }
        else
        {
            await ViewModel.LoadAsync();
        }
    }

    private void OnDetailClosed(object? sender, EventArgs e) => ViewModel.CloseDetail();

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => ViewModel.SearchCommand.Execute(null);

    private void OnRevisionsExpanding(Expander sender, ExpanderExpandingEventArgs args) => ViewModel.LoadRevisionsCommand.Execute(null);

    private void OnCategoriesClick(object sender, RoutedEventArgs e) => ViewModel.ShowCategories = CategoriesToggle.IsChecked == true;

    private void OnTocClick(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton { Content: string text })
        {
            Reader.BringHeadingIntoView(text);
        }
    }

    private async void OnInsertImageClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail, SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (var ext in s_imageTypes)
        {
            picker.FileTypeFilter.Add(ext);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        if (await picker.PickSingleFileAsync() is { } file)
        {
            var props = await file.GetBasicPropertiesAsync();
            await using var stream = await file.OpenStreamForReadAsync();
            await ViewModel.InsertImageAsync(stream, file.Name, (long)props.Size);
        }
    }

    /// <summary>Neue Seite: Titel, Format, Elternseite, Inhalt, Zusammenfassung.</summary>
    private async void OnNewPageClick(object sender, RoutedEventArgs e)
    {
        var l = App.Services.Localizer;
        var title = new TextBox { Header = l.GetString(StringKeys.WikiNewTitle), MaxLength = 200 };
        var format = new ComboBox
        {
            Header = l.GetString(StringKeys.WikiNewFormat),
            ItemsSource = s_formats.Select(f => new SettingsOption(f, l.GetString("Wiki_Format_" + f))).ToList(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var parents = ViewModel.ParentOptions();
        var parent = new ComboBox { Header = l.GetString(StringKeys.WikiNewParent), ItemsSource = parents, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var content = new TextBox
        {
            Header = l.GetString(StringKeys.WikiNewContent),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 200,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
        };
        var summary = new TextBox { Header = l.GetString(StringKeys.WikiNewSummary) };
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = l.GetString(StringKeys.WikiNewPageTitle),
            Content = new ScrollViewer
            {
                MaxHeight = 560,
                Content = new StackPanel { Spacing = 12, MinWidth = 460, Children = { title, format, parent, content, summary, error } },
            },
            PrimaryButtonText = l.GetString(StringKeys.WikiNewCreate),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                if (string.IsNullOrWhiteSpace(title.Text))
                {
                    args.Cancel = true;
                    error.Message = l.GetString(StringKeys.WikiNewTitleMissing);
                    error.IsOpen = true;
                    return;
                }

                var parentId = (parent.SelectedItem as SettingsOption)?.Value;
                var ok = await ViewModel.CreatePageAsync(
                    title.Text, ((SettingsOption)format.SelectedItem).Value, string.IsNullOrEmpty(parentId) ? null : parentId, content.Text, summary.Text);
                if (!ok)
                {
                    args.Cancel = true;
                    error.Message = ViewModel.FeedbackMessage;
                    error.IsOpen = true;
                }
            }
            finally
            {
                deferral.Complete();
            }
        };
        await dialog.ShowAsync();
    }
}
