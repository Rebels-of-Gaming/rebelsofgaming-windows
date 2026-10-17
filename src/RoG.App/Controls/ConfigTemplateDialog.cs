using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.Core.Features.Control;
using RoG.Core.Localization;

namespace RoG.App.Controls;

/// <summary>
/// Formular-Vorlage einer Config-Datei (Guide 18): Felder je Typ (Text, Passwort, Zahl, Ja/Nein, Auswahl, Liste),
/// Prüfung wie die Website über <see cref="ConfigTemplateService.Build"/>, Anwenden über den Aufrufer.
/// </summary>
public static class ConfigTemplateDialog
{
    /// <param name="apply">Sendet die Werte (mit Notiz); <c>false</c> = Fehler, Text dann aus <paramref name="lastError"/>.</param>
    public static async Task ShowAsync(
        XamlRoot root, ConfigTemplateForm form, ILocalizer l, Func<IReadOnlyDictionary<string, object?>, string?, Task<bool>> apply, Func<string> lastError)
    {
        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        if (form.Description is { } description)
        {
            panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["RoGMutedTextStyle"] });
        }

        var readers = new Dictionary<string, Func<TemplateInput>>(StringComparer.Ordinal);
        foreach (var field in form.Fields)
        {
            switch (field.Type)
            {
                case "bool":
                    var toggle = new ToggleSwitch { Header = field.Label, IsOn = form.BoolOf(field.Name) };
                    panel.Children.Add(toggle);
                    readers[field.Name] = () => new TemplateInput(string.Empty, Checked: toggle.IsOn);
                    break;
                case "select":
                    var combo = new ComboBox { Header = field.Label, ItemsSource = field.Options, SelectedItem = form.TextOf(field.Name), HorizontalAlignment = HorizontalAlignment.Stretch };
                    panel.Children.Add(combo);
                    readers[field.Name] = () => new TemplateInput(combo.SelectedItem as string ?? string.Empty);
                    break;
                case "number":
                    var number = new NumberBox
                    {
                        Header = field.Label,
                        Minimum = field.Min ?? 0,
                        Maximum = field.Max ?? 65535,
                        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                        Value = double.TryParse(form.TextOf(field.Name), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : double.NaN,
                    };
                    panel.Children.Add(number);
                    readers[field.Name] = () => new TemplateInput(double.IsNaN(number.Value) ? string.Empty : ((int)number.Value).ToString(CultureInfo.InvariantCulture));
                    break;
                case "list":
                    var list = new TextBox
                    {
                        Header = field.Label,
                        Text = form.TextOf(field.Name),
                        AcceptsReturn = true,
                        IsSpellCheckEnabled = false,
                        MinHeight = 120,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        PlaceholderText = l.GetString(StringKeys.TemplateListHint),
                    };
                    panel.Children.Add(list);
                    readers[field.Name] = () => new TemplateInput(list.Text.Replace("\r", string.Empty, StringComparison.Ordinal));
                    break;
                case "password":
                    var password = new PasswordBox
                    {
                        Header = field.Label,
                        MaxLength = field.Max ?? 128,
                        PlaceholderText = l.GetString(form.PasswordSet.Contains(field.Name) ? StringKeys.TemplatePasswordKeep : StringKeys.TemplatePasswordNone),
                    };
                    panel.Children.Add(password);
                    var clear = new CheckBox { Content = l.GetString(StringKeys.TemplatePasswordClear), Visibility = form.PasswordSet.Contains(field.Name) ? Visibility.Visible : Visibility.Collapsed };
                    panel.Children.Add(clear);
                    readers[field.Name] = () => new TemplateInput(password.Password, ClearPassword: clear.IsChecked == true);
                    break;
                default:
                    var text = new TextBox { Header = field.Label, Text = form.TextOf(field.Name), MaxLength = field.Max ?? 128 };
                    panel.Children.Add(text);
                    readers[field.Name] = () => new TemplateInput(text.Text);
                    break;
            }
        }

        var note = new TextBox { Header = l.GetString(StringKeys.TemplateNote) };
        panel.Children.Add(note);
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = form.Label,
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            PrimaryButtonText = l.GetString(StringKeys.TemplateApply),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                var (values, errors) = ConfigTemplateService.Build(form.Fields, readers.ToDictionary(r => r.Key, r => r.Value()));
                if (errors.Count > 0)
                {
                    args.Cancel = true;
                    var labels = form.Fields.Where(f => errors.Contains(f.Name)).Select(f => f.Label);
                    error.Message = string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.TemplateInvalid), string.Join(", ", labels));
                    error.IsOpen = true;
                    return;
                }

                if (!await apply(values, note.Text))
                {
                    args.Cancel = true;
                    error.Message = lastError();
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
}
