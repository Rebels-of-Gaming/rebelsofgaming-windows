using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RoG.Core.Localization;

namespace RoG.App.Views;

/// <summary>Leerzustand „Kommt bald“ für Bereiche späterer Stufen. Parameter: Übersetzungsschlüssel des Titels.</summary>
public sealed partial class PlaceholderPage : Page
{
    public PlaceholderPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var localizer = App.Services.Localizer;
        var titleKey = e.Parameter as string ?? StringKeys.AppDisplayName;
        TitleText.Text = localizer.GetString(titleKey).ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        ComingSoonText.Text = localizer.GetString(StringKeys.ComingSoon);
    }
}
