using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;
using Windows.System;

namespace RoG.App.Views;

/// <summary>Galerie und Downloads; Vollbild mit Pfeiltasten, Esc und Zoom (Strg+Mausrad / Pinch).</summary>
public sealed partial class MediaPage : Page
{
    public MediaPage()
    {
        ViewModel = new MediaViewModel(App.Services);
        InitializeComponent();
    }

    public MediaViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private async void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (GalleryPanel is null || DownloadsPanel is null)
        {
            return; // feuert schon beim Aufbau
        }

        var downloads = sender.SelectedItem is not null && sender.Items.IndexOf(sender.SelectedItem) == 1;
        GalleryPanel.Visibility = downloads ? Visibility.Collapsed : Visibility.Visible;
        DownloadsPanel.Visibility = downloads ? Visibility.Visible : Visibility.Collapsed;
        if (downloads)
        {
            await ViewModel.Downloads.LoadAsync(silent: true);
        }
    }

    /// <summary>Bilder/Clips einreichen: Dateien wählen, Rechte bestätigen (Pflicht), optional Bildunterschrift.</summary>
    private async void OnSubmitClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker { ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail };
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".mp4", ".webm", ".mov" })
        {
            picker.FileTypeFilter.Add(ext);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        var files = await picker.PickMultipleFilesAsync();
        if (files is not { Count: > 0 })
        {
            return;
        }

        var l = App.Services.Localizer;
        var caption = new TextBox { Header = l.GetString(RoG.Core.Localization.StringKeys.GalleryCaption), MaxLength = 500 };
        var rights = new CheckBox { Content = l.GetString(RoG.Core.Localization.StringKeys.GalleryRights) };
        var panel = new StackPanel { Spacing = 12, MinWidth = 380 };
        panel.Children.Add(new TextBlock { Text = string.Join(", ", files.Select(f => f.Name)), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(caption);
        panel.Children.Add(rights);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = l.GetString(RoG.Core.Localization.StringKeys.GallerySubmitTitle),
            Content = panel,
            PrimaryButtonText = l.GetString(RoG.Core.Localization.StringKeys.GallerySubmitConfirm),
            CloseButtonText = l.GetString(RoG.Core.Localization.StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        rights.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
        rights.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var items = new List<(Func<Task<Stream>> Open, string Name, long Size)>();
        foreach (var file in files)
        {
            var props = await file.GetBasicPropertiesAsync();
            items.Add((() => file.OpenStreamForReadAsync(), file.Name, (long)props.Size));
        }

        await ViewModel.SubmitAsync(items, caption.Text);
    }

    private void OnAlbumClick(object sender, RoutedEventArgs e) => ViewModel.Album = (sender as FrameworkElement)?.Tag as AlbumItemViewModel;

    private void OnItemClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Viewed = (sender as FrameworkElement)?.Tag as GalleryItemViewModel;
        Viewer.Focus(FocusState.Programmatic);
    }

    private void OnPreviousClick(object sender, RoutedEventArgs e) => ViewModel.Step(-1);

    private void OnNextClick(object sender, RoutedEventArgs e) => ViewModel.Step(1);

    private void OnViewerKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ViewModel.IsViewerOpen)
        {
            return;
        }

        args.Handled = true;
        switch (sender.Key)
        {
            case VirtualKey.Escape:
                ViewModel.Viewed = null;
                break;
            case VirtualKey.Left:
                ViewModel.Step(-1);
                break;
            case VirtualKey.Right:
                ViewModel.Step(1);
                break;
        }
    }
}
