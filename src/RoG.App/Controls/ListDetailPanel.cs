using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using RoG.Core.Localization;
using Windows.System;

namespace RoG.App.Controls;

/// <summary>
/// Liste-Detail wie im Pflichtenheft: ab <see cref="WideThreshold"/> epx Liste links und Detail rechts nebeneinander,
/// darunter nur eine Spalte – mit offenem Detail ersetzt es die Liste und zeigt „Zurück“ (auch Alt+← und Esc).
/// </summary>
[Microsoft.UI.Xaml.Markup.ContentProperty(Name = nameof(Detail))]
public sealed partial class ListDetailPanel : UserControl
{
    public const double WideThreshold = 760;

    public static readonly DependencyProperty ListProperty = DependencyProperty.Register(
        nameof(List), typeof(UIElement), typeof(ListDetailPanel), new PropertyMetadata(null, (d, e) => ((ListDetailPanel)d)._listHost.Content = e.NewValue));

    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(UIElement), typeof(ListDetailPanel), new PropertyMetadata(null, (d, e) => ((ListDetailPanel)d)._detailHost.Content = e.NewValue));

    public static readonly DependencyProperty IsDetailOpenProperty = DependencyProperty.Register(
        nameof(IsDetailOpen), typeof(bool), typeof(ListDetailPanel), new PropertyMetadata(false, (d, _) => ((ListDetailPanel)d).UpdateLayoutMode()));

    public static readonly DependencyProperty ListWidthProperty = DependencyProperty.Register(
        nameof(ListWidth), typeof(double), typeof(ListDetailPanel), new PropertyMetadata(380d, (d, _) => ((ListDetailPanel)d).UpdateLayoutMode()));

    private readonly Grid _grid = new();
    private readonly ContentControl _listHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, IsTabStop = false };
    private readonly ContentControl _detailHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, IsTabStop = false };
    private readonly Grid _detailColumn = new();
    private readonly Button _back = new();
    private bool _wide = true;

    public ListDetailPanel()
    {
        _grid.ColumnDefinitions.Add(new ColumnDefinition());
        _grid.ColumnDefinitions.Add(new ColumnDefinition());

        var divider = new Border { BorderBrush = (Brush)Application.Current.Resources["RoGLineBrush"], BorderThickness = new Thickness(0, 0, 1, 0) };
        Grid.SetColumn(divider, 0);

        _back.Content = new FontIcon { Glyph = "", FontSize = 16 };
        _back.Style = (Style)Application.Current.Resources["RoGSecondaryButtonStyle"];
        _back.Margin = new Thickness(16, 12, 0, 0);
        _back.HorizontalAlignment = HorizontalAlignment.Left;
        var backText = App.Services.Localizer.GetString(StringKeys.CommonBack);
        AutomationProperties.SetName(_back, backText);
        ToolTipService.SetToolTip(_back, backText);
        _back.Click += (_, _) => CloseDetail();

        _detailColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _detailColumn.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(_detailHost, 1);
        _detailColumn.Children.Add(_back);
        _detailColumn.Children.Add(_detailHost);
        Grid.SetColumn(_detailColumn, 1);

        _grid.Children.Add(_listHost);
        _grid.Children.Add(divider);
        _grid.Children.Add(_detailColumn);
        Content = _grid;

        var altLeft = new KeyboardAccelerator { Key = VirtualKey.Left, Modifiers = VirtualKeyModifiers.Menu };
        altLeft.Invoked += OnBackAccelerator;
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += OnBackAccelerator;
        KeyboardAccelerators.Add(altLeft);
        KeyboardAccelerators.Add(escape);

        SizeChanged += (_, e) =>
        {
            var wide = e.NewSize.Width >= WideThreshold;
            if (wide != _wide)
            {
                _wide = wide;
                UpdateLayoutMode();
            }
        };
        UpdateLayoutMode();
    }

    /// <summary>„Zurück“ im schmalen Fenster – die Seite hebt die Auswahl auf.</summary>
    public event EventHandler? DetailClosed;

    public UIElement? List
    {
        get => (UIElement?)GetValue(ListProperty);
        set => SetValue(ListProperty, value);
    }

    public UIElement? Detail
    {
        get => (UIElement?)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public bool IsDetailOpen
    {
        get => (bool)GetValue(IsDetailOpenProperty);
        set => SetValue(IsDetailOpenProperty, value);
    }

    public double ListWidth
    {
        get => (double)GetValue(ListWidthProperty);
        set => SetValue(ListWidthProperty, value);
    }

    private void OnBackAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_wide && IsDetailOpen)
        {
            args.Handled = true;
            CloseDetail();
        }
    }

    private void CloseDetail()
    {
        IsDetailOpen = false;
        DetailClosed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateLayoutMode()
    {
        if (_wide)
        {
            _grid.ColumnDefinitions[0].Width = new GridLength(ListWidth);
            _grid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            _listHost.Visibility = Visibility.Visible;
            _detailColumn.Visibility = Visibility.Visible;
            Grid.SetColumn(_detailColumn, 1);
            _back.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Eine Spalte: entweder Liste oder Detail
            _grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            _grid.ColumnDefinitions[1].Width = new GridLength(0);
            _listHost.Visibility = IsDetailOpen ? Visibility.Collapsed : Visibility.Visible;
            _detailColumn.Visibility = IsDetailOpen ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(_detailColumn, 0);
            _back.Visibility = Visibility.Visible;
        }
    }
}
