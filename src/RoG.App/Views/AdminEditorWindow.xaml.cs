using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.App.ViewModels;
using RoG.Core.Features.Admin;
using RoG.Core.Localization;

namespace RoG.App.Views;

/// <summary>Ein Bereich der Website-Verwaltung im eigenen Fenster; je Bereich höchstens eins.</summary>
public sealed partial class AdminEditorWindow : Window
{
    private static readonly Dictionary<string, AdminEditorWindow> s_open = new(StringComparer.Ordinal);

    private AdminEditorWindow(AdminArea area)
    {
        ViewModel = new AdminEditorViewModel(App.Services, area) { Confirm = ConfirmAsync };
        InitializeComponent();
        TitleText.Text = ViewModel.Title.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        Title = ViewModel.Title;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1040, 720));
        if (area.IsSingleton)
        {
            // Einstellungen: Formular über die ganze Breite
            Grid.SetColumn(FormPanel, 0);
            Grid.SetColumnSpan(FormPanel, 2);
        }

        Closed += (_, _) => s_open.Remove(area.Key);
        _ = ViewModel.LoadAsync();
    }

    public AdminEditorViewModel ViewModel { get; }

    public static void Open(AdminArea area)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (!s_open.TryGetValue(area.Key, out var window))
        {
            window = new AdminEditorWindow(area);
            s_open[area.Key] = window;
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
            Content = text,
            PrimaryButtonText = l.GetString(StringKeys.AdminFormDeleteConfirm),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
