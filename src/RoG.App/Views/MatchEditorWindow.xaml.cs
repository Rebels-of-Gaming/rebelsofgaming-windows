using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.App.ViewModels;
using RoG.Core.Localization;
using Windows.Storage.Pickers;

namespace RoG.App.Views;

/// <summary>Match eintragen/bearbeiten im eigenen Fenster; nach dem Schließen lädt die Matches-Seite neu.</summary>
public sealed partial class MatchEditorWindow : Window
{
    private static readonly string[] s_fileTypes = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".pdf", ".zip", ".7z"];

    private MatchEditorWindow(Action<bool> closed)
    {
        ViewModel = new MatchEditorViewModel(App.Services) { Confirm = ConfirmAsync };
        InitializeComponent();
        Title = App.Services.Localizer.GetString(StringKeys.MatchEditNew);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1080, 800));
        Closed += (_, _) => closed(ViewModel.Changed);
    }

    public MatchEditorViewModel ViewModel { get; }

    /// <summary>Neues Match (<paramref name="matchId"/> <c>null</c>) oder vorhandenes bearbeiten.</summary>
    public static void Open(string? matchId, Action<bool> closed)
    {
        var window = new MatchEditorWindow(closed);
        _ = window.ViewModel.StartAsync(matchId);
        window.Activate();
    }

    private async void OnAttachClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (var ext in s_fileTypes)
        {
            picker.FileTypeFilter.Add(ext);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        if (files is not { Count: > 0 })
        {
            return;
        }

        var streams = new List<(Stream, string)>();
        try
        {
            foreach (var file in files)
            {
                streams.Add((await file.OpenStreamForReadAsync(), file.Name));
            }

            await ViewModel.AttachAsync(streams);
        }
        finally
        {
            foreach (var (stream, _) in streams)
            {
                await stream.DisposeAsync();
            }
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
