using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using RoG.Core.Features.About;
using RoG.Core.Localization;

namespace RoG.App.Views;

/// <summary>
/// Funktions-Tour (<see cref="FeatureTour"/>): eine Karte nach der anderen im Dialog, „Weiter“/„Zurück“/„Überspringen“,
/// mit Tastatur bedienbar; der Kartenwechsel wird dem Sprachausgabe-Programm angesagt.
/// </summary>
public static class FeatureTourDialog
{
    public static async Task ShowAsync(XamlRoot root, ILocalizer l)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(l);
        var cards = FeatureTour.Cards;
        var index = 0;

        var icon = new FontIcon { FontSize = 40, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["RoGGoldBrush"], HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        var title = new TextBlock { Style = (Style)Application.Current.Resources["RoGSectionTextStyle"], TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        var text = new TextBlock { Style = (Style)Application.Current.Resources["RoGBodyTextStyle"], TextWrapping = TextWrapping.Wrap };
        var step = new TextBlock { Style = (Style)Application.Current.Resources["RoGMutedTextStyle"] };
        var panel = new StackPanel { Spacing = 12, MaxWidth = 420, MinHeight = 180 };
        panel.Children.Add(icon);
        panel.Children.Add(title);
        panel.Children.Add(text);
        panel.Children.Add(step);
        AutomationProperties.SetLiveSetting(panel, AutomationLiveSetting.Polite);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = l.GetString(StringKeys.TourTitle),
            Content = panel,
            SecondaryButtonText = l.GetString(StringKeys.TourBack),
            CloseButtonText = l.GetString(StringKeys.TourSkip),
            DefaultButton = ContentDialogButton.Primary,
        };

        void Show()
        {
            var card = cards[index];
            icon.Glyph = card.Glyph;
            title.Text = l.GetString($"Tour_{card.Key}_Title");
            text.Text = l.GetString($"Tour_{card.Key}_Text");
            step.Text = string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.TourStep), index + 1, cards.Count);
            AutomationProperties.SetName(panel, $"{title.Text}. {text.Text} {step.Text}");
            dialog.PrimaryButtonText = l.GetString(index == cards.Count - 1 ? StringKeys.TourDone : StringKeys.TourNext);
            dialog.IsSecondaryButtonEnabled = index > 0;
            if (FrameworkElementAutomationPeer.FromElement(panel) is { } peer)
            {
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        }

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (index < cards.Count - 1)
            {
                args.Cancel = true;
                index++;
                Show();
            }
        };
        dialog.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            if (index > 0)
            {
                index--;
                Show();
            }
        };

        Show();
        await dialog.ShowAsync();
    }
}
