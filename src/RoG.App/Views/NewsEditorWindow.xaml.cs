using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.App.ViewModels;
using RoG.Core.Features.News;
using RoG.Core.Localization;
using Windows.Storage.Pickers;

namespace RoG.App.Views;

/// <summary>News-Redaktion im eigenen Fenster (höchstens eins). Nach dem Schließen lädt die News-Seite neu.</summary>
public sealed partial class NewsEditorWindow : Window
{
    private static readonly string[] s_imageTypes = [".png", ".jpg", ".jpeg", ".webp"];
    private static NewsEditorWindow? s_open;

    private NewsEditorWindow(NewsRights rights, Action<bool> closed)
    {
        ViewModel = new NewsEditorViewModel(App.Services, rights) { Confirm = ConfirmAsync };
        InitializeComponent();
        Title = App.Services.Localizer.GetString(StringKeys.NewsEditWindow);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 760));
        Closed += (_, _) =>
        {
            s_open = null;
            closed(ViewModel.Changed);
        };
    }

    public NewsEditorViewModel ViewModel { get; }

    /// <summary>Redaktion öffnen, optional direkt mit einer News (Slug).</summary>
    public static void Open(NewsRights rights, string? slug, Action<bool> closed)
    {
        if (s_open is null)
        {
            s_open = new NewsEditorWindow(rights, closed);
            _ = s_open.ViewModel.StartAsync(slug);
        }
        else if (slug is not null)
        {
            _ = s_open.ViewModel.StartAsync(slug);
        }

        s_open.Activate();
    }

    private async void OnSetCoverClick(object sender, RoutedEventArgs e)
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
            await ViewModel.SetCoverAsync(stream, file.Name, (long)props.Size);
        }
    }

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
