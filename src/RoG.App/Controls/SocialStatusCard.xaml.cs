using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.App.ViewModels;
using RoG.Core.Localization;

namespace RoG.App.Controls;

public sealed partial class SocialStatusCard : UserControl
{
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(SocialStatusViewModel), typeof(SocialStatusCard), new PropertyMetadata(null, OnStatusChanged));

    public SocialStatusCard() => InitializeComponent();

    public SocialStatusViewModel? Status
    {
        get => (SocialStatusViewModel?)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SocialStatusCard card && e.NewValue is SocialStatusViewModel status)
        {
            status.Confirm = card.ConfirmAsync;
        }
    }

    private async Task<bool> ConfirmAsync(string title, string text)
    {
        if (XamlRoot is null)
        {
            return false;
        }

        var l = App.Services.Localizer;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = l.GetString(StringKeys.SocialRepostConfirm),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
