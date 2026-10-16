using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;
using RoG.Core.Localization;

namespace RoG.App.Views;

public sealed partial class EventsPage : Page
{
    public EventsPage()
    {
        ViewModel = new EventsViewModel(App.Services);
        InitializeComponent();
        ViewModel.Detail.ConfirmSeries = ConfirmSeriesAsync;
    }

    private static string L(string key) => App.Services.Localizer.GetString(key);

    /// <summary>Neuer Termin: Typ, Bereich (clanweit/Squad), Titel, Zeit (Europe/Luxembourg), Dauer, Ort, Serie.</summary>
    private async void OnNewEventClick(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<SettingsOption> types, scopes;
        try
        {
            (types, scopes) = await ViewModel.CreateOptionsAsync();
        }
        catch (Exception ex) when (ex is RoG.Core.Api.ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            return;
        }

        if (types.Count == 0 || scopes.Count == 0)
        {
            return;
        }

        var type = new ComboBox { Header = L(StringKeys.EventsNewType), ItemsSource = types, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var scope = new ComboBox { Header = L(StringKeys.EventsNewScope), ItemsSource = scopes, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var title = new TextBox { Header = L(StringKeys.EventsNewTitle), MaxLength = 120 };
        var date = new CalendarDatePicker { Header = L(StringKeys.EventsNewDate), Date = DateTimeOffset.Now.Date.AddDays(1), HorizontalAlignment = HorizontalAlignment.Stretch };
        var time = new TimePicker { Header = L(StringKeys.EventsNewTime), Time = new TimeSpan(20, 0, 0), ClockIdentifier = "24HourClock" };
        var duration = new NumberBox { Header = L(StringKeys.EventsNewDuration), Value = 120, Minimum = 15, Maximum = 1440, SmallChange = 15, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var locationType = new ComboBox
        {
            Header = L(StringKeys.EventsNewLocationType),
            ItemsSource = RoG.Core.Features.Events.EventAdminService.LocationTypes.Select(t => new SettingsOption(t, L("EventsNew_Location_" + t))).ToList(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var location = new TextBox { Header = L(StringKeys.EventsNewLocation) };
        var mandatory = new CheckBox { Content = L(StringKeys.EventsNewMandatory) };
        var weekly = new CheckBox { Content = L(StringKeys.EventsNewWeekly) };
        var count = new NumberBox { Header = L(StringKeys.EventsNewCount), Value = 8, Minimum = 1, Maximum = 52, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Visibility = Visibility.Collapsed };
        weekly.Checked += (_, _) => count.Visibility = Visibility.Visible;
        weekly.Unchecked += (_, _) => count.Visibility = Visibility.Collapsed;
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };

        var panel = new StackPanel { Spacing = 10, MinWidth = 420 };
        foreach (var control in new UIElement[] { type, scope, title, date, time, duration, locationType, location, mandatory, weekly, count, error })
        {
            panel.Children.Add(control);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = L(StringKeys.EventsNewDialogTitle),
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            PrimaryButtonText = L(StringKeys.EventsNewCreate),
            CloseButtonText = L(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                if (string.IsNullOrWhiteSpace(title.Text) || date.Date is null)
                {
                    args.Cancel = true;
                    error.Message = L(StringKeys.ApplyIncomplete);
                    error.IsOpen = true;
                    return;
                }

                var start = date.Date.Value.Date + time.Time;
                var draft = new RoG.Core.Features.Events.EventDraft(
                    ((SettingsOption)type.SelectedItem).Value,
                    title.Text,
                    start,
                    double.IsNaN(duration.Value) ? 120 : (int)duration.Value,
                    SquadId: ((SettingsOption)scope.SelectedItem).Value is { Length: > 0 } squad ? squad : null,
                    LocationType: ((SettingsOption)locationType.SelectedItem).Value,
                    Location: location.Text,
                    Mandatory: mandatory.IsChecked == true,
                    WeeklyDays: weekly.IsChecked == true ? [(int)start.DayOfWeek == 0 ? 7 : (int)start.DayOfWeek] : null,
                    Count: weekly.IsChecked == true && !double.IsNaN(count.Value) ? (int)count.Value : null);
                if (await ViewModel.CreateAsync(draft) is { } message)
                {
                    args.Cancel = true;
                    error.Message = message;
                    error.IsOpen = true;
                }
            }
            finally
            {
                deferral.Complete();
            }
        };
        await dialog.ShowAsync();
    }

    private async Task<(bool Ok, bool Series)> ConfirmSeriesAsync(string title, string text)
    {
        var series = new CheckBox { Content = L(StringKeys.EventsWholeSeries) };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(series);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = panel,
            PrimaryButtonText = L(StringKeys.CommonConfirm),
            CloseButtonText = L(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Close,
        };
        var ok = await dialog.ShowAsync() == ContentDialogResult.Primary;
        return (ok, series.IsChecked == true);
    }

    public EventsViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string { Length: > 0 } id)
        {
            await ViewModel.OpenAsync(id);
        }
        else
        {
            await ViewModel.LoadAsync();
        }
    }

    private void OnDetailClosed(object? sender, EventArgs e) => ViewModel.Selected = null;

    private async void OnRefreshInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.LoadAsync();
    }
}
