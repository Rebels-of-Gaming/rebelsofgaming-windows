using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using RoG.Core.Localization;

namespace RoG.App.Controls;

/// <summary>
/// Einheitlicher Lade-, Fehler- und Leerzustand für Listen und Details (Pflicht für jede Liste laut CLAUDE.md).
/// Liegt über bzw. statt dem Inhalt; zeigt nichts, solange Daten da sind.
/// </summary>
public sealed partial class StateView : UserControl
{
    public static readonly DependencyProperty IsLoadingProperty = Register(nameof(IsLoading), typeof(bool), false);
    public static readonly DependencyProperty ErrorMessageProperty = Register(nameof(ErrorMessage), typeof(string), null);
    public static readonly DependencyProperty IsEmptyProperty = Register(nameof(IsEmpty), typeof(bool), false);
    public static readonly DependencyProperty EmptyTextProperty = Register(nameof(EmptyText), typeof(string), null);
    public static readonly DependencyProperty RetryCommandProperty = Register(nameof(RetryCommand), typeof(ICommand), null);

    private readonly ProgressRing _ring = new() { Width = 40, Height = 40, IsActive = false };
    private readonly StackPanel _error = new() { Spacing = 12, MaxWidth = 480 };
    private readonly TextBlock _errorText = new() { TextAlignment = TextAlignment.Center };
    private readonly Button _retry = new();
    private readonly StackPanel _empty = new() { Spacing = 12, MaxWidth = 420 };
    private readonly TextBlock _emptyText = new() { TextAlignment = TextAlignment.Center };

    public StateView()
    {
        var localizer = App.Services.Localizer;
        _errorText.Style = (Style)Application.Current.Resources["RoGBodyTextStyle"];
        _emptyText.Style = (Style)Application.Current.Resources["RoGMutedTextStyle"];
        _retry.Style = (Style)Application.Current.Resources["RoGPrimaryButtonStyle"];
        _retry.Content = localizer.GetString(StringKeys.CommonRetry);
        _retry.HorizontalAlignment = HorizontalAlignment.Center;
        _retry.Click += (_, _) =>
        {
            if (RetryCommand?.CanExecute(null) == true)
            {
                RetryCommand.Execute(null);
            }
        };
        AutomationProperties.SetName(_ring, localizer.GetString(StringKeys.CommonLoading));

        _error.Children.Add(_errorText);
        _error.Children.Add(_retry);

        var badge = new Image { Width = 80, Height = 80, Opacity = 0.35, Source = new BitmapImage(new Uri("ms-appx:///Assets/rog-badge-512.png")) };
        AutomationProperties.SetAccessibilityView(badge, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        _empty.Children.Add(badge);
        _empty.Children.Add(_emptyText);

        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 48, 16, 48) };
        grid.Children.Add(_ring);
        grid.Children.Add(_error);
        grid.Children.Add(_empty);
        Content = grid;
        IsTabStop = false;
        Update();
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public string? ErrorMessage
    {
        get => (string?)GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    public bool IsEmpty
    {
        get => (bool)GetValue(IsEmptyProperty);
        set => SetValue(IsEmptyProperty, value);
    }

    public string? EmptyText
    {
        get => (string?)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => (ICommand?)GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    private static DependencyProperty Register(string name, Type type, object? fallback) =>
        DependencyProperty.Register(name, type, typeof(StateView), new PropertyMetadata(fallback, (d, _) => ((StateView)d).Update()));

    private void Update()
    {
        var hasError = !string.IsNullOrEmpty(ErrorMessage);
        _ring.IsActive = IsLoading;
        _ring.Visibility = IsLoading ? Visibility.Visible : Visibility.Collapsed;
        _errorText.Text = ErrorMessage ?? string.Empty;
        _error.Visibility = !IsLoading && hasError ? Visibility.Visible : Visibility.Collapsed;
        _emptyText.Text = EmptyText ?? App.Services.Localizer.GetString(StringKeys.CommonEmpty);
        _empty.Visibility = !IsLoading && !hasError && IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        Visibility = IsLoading || hasError || IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }
}
