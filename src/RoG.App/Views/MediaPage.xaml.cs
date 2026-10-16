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
