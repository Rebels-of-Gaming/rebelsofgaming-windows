using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using RoG.Core.Api;
using RoG.Core.Features.Applications;
using RoG.Core.Localization;

namespace RoG.App.Controls;

/// <summary>
/// Bewerbung als Dialog, aus dem Formular der API gebaut (Guide 07): Fragen je Typ, Geburtsdatum, Regelwerke bestätigen,
/// Trial-Zustimmung, Werber. Fehler der API (Discord fehlt, Sperre, Alter …) erscheinen im Dialog.
/// </summary>
public static class ApplicationDialog
{
    public static async Task<bool> ShowAsync(XamlRoot root, string squadSlug)
    {
        var services = App.Services;
        var l = services.Localizer;
        var forms = new ApplicationService(services.Api.Client, services.Api.Http);

        ApplicationForm form;
        try
        {
            form = await forms.GetFormAsync(squadSlug);
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            var message = ex is ApiProblemException p ? l.GetError(p.Problem.Code) : l.GetError(ApiProblem.Codes.Unknown);
            await new ContentDialog { XamlRoot = root, Title = l.GetString(StringKeys.ApplyTitle), Content = message, CloseButtonText = l.GetString(StringKeys.CommonCancel) }.ShowAsync();
            return false;
        }

        var panel = new StackPanel { Spacing = 14, MinWidth = 440 };
        if (form.Waitlist)
        {
            panel.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational, Message = l.GetString(StringKeys.ApplyWaitlist) });
        }

        var birthdate = new CalendarDatePicker { Header = string.Format(System.Globalization.CultureInfo.CurrentCulture, l.GetString(StringKeys.ApplyBirthdate), form.MinAge), HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(birthdate);

        var answers = new Dictionary<string, Func<string?>>();
        foreach (var q in form.Questions)
        {
            var header = q.Required ? q.Label + " *" : q.Label;
            FrameworkElement input;
            switch (q.Type)
            {
                case "select" when q.Options.Count > 0:
                    var combo = new ComboBox { Header = header, ItemsSource = q.Options.Select(o => o.Label).ToList(), HorizontalAlignment = HorizontalAlignment.Stretch };
                    answers[q.Id] = () => combo.SelectedIndex >= 0 ? q.Options[combo.SelectedIndex].Value : null;
                    input = combo;
                    break;
                case "yesno" or "boolean" or "checkbox":
                    var yesNo = new ComboBox { Header = header, ItemsSource = new[] { l.GetString(StringKeys.ApplyYes), l.GetString(StringKeys.ApplyNo) }, HorizontalAlignment = HorizontalAlignment.Stretch };
                    answers[q.Id] = () => yesNo.SelectedIndex switch { 0 => "yes", 1 => "no", _ => null };
                    input = yesNo;
                    break;
                case "number":
                    var number = new NumberBox { Header = header, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
                    answers[q.Id] = () => double.IsNaN(number.Value) ? null : number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    input = number;
                    break;
                default:
                    var text = new TextBox { Header = header, AcceptsReturn = q.Type is "textarea" or "longtext", TextWrapping = TextWrapping.Wrap, MinHeight = q.Type is "textarea" or "longtext" ? 90 : 0 };
                    answers[q.Id] = () => text.Text;
                    input = text;
                    break;
            }

            if (q.Help is not null)
            {
                ToolTipService.SetToolTip(input, q.Help);
            }

            panel.Children.Add(input);
        }

        var rules = new List<(FormRule Rule, CheckBox Box)>();
        foreach (var rule in form.Rules)
        {
            var box = new CheckBox { Content = string.Format(System.Globalization.CultureInfo.CurrentCulture, l.GetString(StringKeys.ApplyAcceptRules), rule.Title) };
            var link = new HyperlinkButton { Content = l.GetString(StringKeys.ApplyReadRules), Padding = new Thickness(0) };
            link.Click += (_, _) => _ = services.Browser.OpenAsync(new Uri(services.Environment.WebBaseUrl, $"{services.Language}/rules/{Uri.EscapeDataString(rule.Slug)}"));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(box);
            row.Children.Add(link);
            panel.Children.Add(row);
            rules.Add((rule, box));
        }

        var trial = new CheckBox { Content = l.GetString(StringKeys.ApplyTrialConsent) };
        panel.Children.Add(trial);
        var referral = new TextBox { Header = l.GetString(StringKeys.ApplyReferral) };
        panel.Children.Add(referral);
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = string.Format(System.Globalization.CultureInfo.CurrentCulture, l.GetString(StringKeys.ApplyTitleFor), form.SquadName),
            Content = new ScrollViewer { Content = panel, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = l.GetString(StringKeys.ApplySubmit),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Primary,
        };

        var submitted = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                var missing = form.Questions.Where(q => q.Required && string.IsNullOrWhiteSpace(answers[q.Id]())).Select(q => q.Label).ToList();
                if (birthdate.Date is null || missing.Count > 0 || rules.Any(r => r.Box.IsChecked != true) || trial.IsChecked != true)
                {
                    args.Cancel = true;
                    error.Message = l.GetString(StringKeys.ApplyIncomplete) + (missing.Count > 0 ? " " + string.Join(", ", missing) : string.Empty);
                    error.IsOpen = true;
                    return;
                }

                await forms.SubmitAsync(
                    form.SquadSlug,
                    DateOnly.FromDateTime(birthdate.Date.Value.Date),
                    answers.ToDictionary(a => a.Key, a => a.Value() ?? string.Empty),
                    rules.Select(r => r.Rule.Id),
                    trial.IsChecked == true,
                    referral.Text);
                submitted = true;
            }
            catch (ApiProblemException ex)
            {
                args.Cancel = true;
                error.Message = ex.Problem.Code is "application_blocked" or "validation_failed" && ex.Problem.Detail is { } detail ? detail : l.GetError(ex.Problem.Code);
                error.IsOpen = true;
            }
            catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
            {
                args.Cancel = true;
                error.Message = l.GetError(ApiProblem.Codes.Unknown);
                error.IsOpen = true;
            }
            finally
            {
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
        return submitted;
    }
}
