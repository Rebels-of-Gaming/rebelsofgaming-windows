using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.App.ViewModels;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;

namespace RoG.App.Views;

/// <summary>„Neuer Gameserver“ bzw. Verwaltung eines über die Website angelegten Servers im eigenen Fenster.</summary>
public sealed partial class ServerProvisionWindow : Window
{
    private static readonly Dictionary<string, ServerProvisionWindow> s_open = new(StringComparer.Ordinal);

    private ServerProvisionWindow(GameServer? server, Action changed)
    {
        ViewModel = new ServerProvisionViewModel(App.Services, server) { Confirm = ConfirmAsync };
        ViewModel.Changed += (_, _) => changed();
        InitializeComponent();
        var title = server?.Name ?? ViewModel.Title;
        TitleText.Text = title.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        Title = title;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(820, 860));
        _ = ViewModel.LoadAsync();
    }

    public ServerProvisionViewModel ViewModel { get; }

    /// <summary>Neuer Server (<paramref name="server"/> <c>null</c>) oder Verwaltung eines angelegten; je Server ein Fenster.</summary>
    public static void Open(GameServer? server, Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        var key = server?.Slug ?? string.Empty;
        if (!s_open.TryGetValue(key, out var window))
        {
            window = new ServerProvisionWindow(server, changed);
            s_open[key] = window;
            window.Closed += (_, _) => s_open.Remove(key);
        }

        window.Activate();
    }

    private async Task<bool> ConfirmAsync(string title, string text)
    {
        var l = App.Services.Localizer;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = l.GetString(StringKeys.ProvisionConfirmYes),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
