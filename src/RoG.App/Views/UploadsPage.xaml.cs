using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.Services;
using RoG.App.ViewModels;
using RoG.Core.Features.Uploads;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace RoG.App.Views;

/// <summary>Uploader: Drag &amp; Drop aus dem Explorer, Dateiauswahl, Strg+V (Screenshot oder kopierte Dateien).</summary>
public sealed partial class UploadsPage : Page
{
    public UploadsPage()
    {
        ViewModel = new UploadsViewModel(App.Services);
        Links = new ShortLinksViewModel(App.Services);
        InitializeComponent();
    }

    public UploadsViewModel ViewModel { get; }

    public ShortLinksViewModel Links { get; }

    private async void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (UploadsPanel is null || LinksPanel is null)
        {
            return; // feuert schon beim Aufbau
        }

        var links = sender.SelectedItem is not null && sender.Items.IndexOf(sender.SelectedItem) == 1;
        UploadsPanel.Visibility = links ? Visibility.Collapsed : Visibility.Visible;
        LinksPanel.Visibility = links ? Visibility.Visible : Visibility.Collapsed;
        if (links)
        {
            await Links.LoadAsync(silent: true);
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
        if (e.Parameter as string == MainWindow.UploadsShareKey && !ViewModel.IsGuest)
        {
            var files = await ShareInbox.PendingAsync();
            try
            {
                await ViewModel.UploadFilesAsync(files);
            }
            finally
            {
                ShareInbox.Clear();
            }
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!ViewModel.IsGuest && e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = App.Services.Localizer.GetString(RoG.Core.Localization.StringKeys.UploadsDropCaption);
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (ViewModel.IsGuest || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        await ViewModel.UploadFilesAsync(items.OfType<StorageFile>().ToList());
    }

    private async void OnPickClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail, SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".mp4", ".webm", ".mov", ".mkv" })
        {
            picker.FileTypeFilter.Add(ext);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        var files = await picker.PickMultipleFilesAsync();
        if (files is { Count: > 0 })
        {
            await ViewModel.UploadFilesAsync(files.ToList());
        }
    }

    private async void OnPasteClick(object sender, RoutedEventArgs e) => await PasteAsync();

    private async void OnPasteInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Strg+V in einem Textfeld (z. B. Suche) nicht abfangen
        if (FocusManager.GetFocusedElement(XamlRoot) is TextBox or AutoSuggestBox)
        {
            return;
        }

        args.Handled = true;
        await PasteAsync();
    }

    /// <summary>Zwischenablage: kopierte Dateien oder ein Bild (Screenshot, Ausschneidetool) als PNG hochladen.</summary>
    private async Task PasteAsync()
    {
        if (ViewModel.IsGuest)
        {
            return;
        }

        var content = Clipboard.GetContent();
        if (content.Contains(StandardDataFormats.StorageItems))
        {
            var items = await content.GetStorageItemsAsync();
            await ViewModel.UploadFilesAsync(items.OfType<StorageFile>().Where(f => UploadService.MimeFor(f.Name) is not null).ToList());
            return;
        }

        if (content.Contains(StandardDataFormats.Bitmap))
        {
            var bitmap = await content.GetBitmapAsync();
            var name = $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png";
            var file = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
            using (var source = await bitmap.OpenReadAsync())
            using (var target = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(source);
                var pixels = await decoder.GetSoftwareBitmapAsync();
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, target);
                encoder.SetSoftwareBitmap(pixels);
                await encoder.FlushAsync();
            }

            await ViewModel.UploadFilesAsync([file]);
        }
    }

    private async void OnRefreshInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.LoadAsync();
    }
}
