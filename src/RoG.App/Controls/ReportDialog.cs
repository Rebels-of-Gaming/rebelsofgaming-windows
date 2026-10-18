using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.App.ViewModels;
using RoG.Core.Api;
using RoG.Core.Features.Moderation;
using RoG.Core.Localization;

namespace RoG.App.Controls;

/// <summary>Inhalt melden (Guide 28): Grund wählen, optional Kommentar; geht an die Moderation, nie an die gemeldete Person.</summary>
public static class ReportDialog
{
    /// <param name="type"><c>message</c>, <c>profile</c>, <c>gallery_item</c>, <c>news_comment</c>, <c>forum_post</c>,
    /// <c>wiki_page</c> (mit <paramref name="locale"/>) oder <c>wiki_revision</c>.</param>
    public static async Task ShowAsync(string type, string id, string? locale = null)
    {
        if (App.ActiveXamlRoot is not { } root)
        {
            return;
        }

        var services = App.Services;
        var l = services.Localizer;
        var reasons = ModerationService.ReportReasons.Select(r => new SettingsOption(r, l.GetString("Report_Reason_" + r))).ToList();
        var reason = new ComboBox { Header = l.GetString(StringKeys.ReportReason), ItemsSource = reasons, HorizontalAlignment = HorizontalAlignment.Stretch };
        var comment = new TextBox { Header = l.GetString(StringKeys.ReportComment), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 1000 };
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        var panel = new StackPanel { Spacing = 12, MinWidth = 380 };
        panel.Children.Add(new TextBlock { Text = l.GetString(StringKeys.ReportHint), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(reason);
        panel.Children.Add(comment);
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = l.GetString(StringKeys.ReportTitle),
            Content = panel,
            PrimaryButtonText = l.GetString(StringKeys.ReportSend),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Primary,
        };
        var sent = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                if (reason.SelectedItem is not SettingsOption chosen)
                {
                    args.Cancel = true;
                    error.Message = l.GetString(StringKeys.ReportReasonMissing);
                    error.IsOpen = true;
                    return;
                }

                await new ModerationService(services.Api.Client).ReportAsync(type, id, chosen.Value, comment.Text, locale);
                sent = true;
            }
            catch (ApiProblemException ex)
            {
                // 429 too_many_attempts (z. B. viele Kinderschutz-Meldungen, v0.30.4) mit Wartezeit aus Retry-After
                args.Cancel = true;
                error.Message = ex.Problem.RetryAfter is { } wait && ex.Problem.IsRateLimited
                    ? l.GetError(ex.Problem.Code) + " " + string.Format(System.Globalization.CultureInfo.CurrentCulture, l.GetString(StringKeys.CommonRetryAfter), Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)))
                    : l.GetError(ex.Problem.Code);
                error.IsOpen = true;
            }
            catch (RoG.Core.Api.Generated.GeneratedApiException)
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

        if (sent)
        {
            await new ContentDialog { XamlRoot = root, Title = l.GetString(StringKeys.ReportThanks), CloseButtonText = "OK" }.ShowAsync();
        }
    }
}
