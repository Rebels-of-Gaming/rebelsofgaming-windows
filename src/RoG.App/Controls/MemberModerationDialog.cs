using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.App.ViewModels;
using RoG.Core.Features.Moderation;
using RoG.Core.Localization;

namespace RoG.App.Controls;

/// <summary>
/// Mitglied moderieren (Guide 28): aktive Punkte, Verwarnungen (zurücknehmen), Sperren (aufheben), neue Verwarnung
/// oder Sperre. Was erlaubt ist, entscheidet der Server (Hierarchie, nie Owner/man selbst).
/// </summary>
public static class MemberModerationDialog
{
    public static async Task ShowAsync(XamlRoot root, ModerationViewModel moderation, string userId, string? reportId)
    {
        var l = moderation.Services.Localizer;
        ModMember? member = null;
        await moderation.RunAsync(async () => member = await moderation.Api.MemberAsync(userId));
        if (member is null)
        {
            return;
        }

        var panel = new StackPanel { Spacing = 12, MinWidth = 460 };
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.ModActivePoints), member.ActivePoints),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["RoGGoldBrush"],
        });

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = member.User.Name,
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
        };

        // Verwarnungen
        panel.Children.Add(Section(l.GetString(StringKeys.ModWarnings)));
        foreach (var w in member.Warnings)
        {
            var row = Row($"{w.Points} P · {w.Reason} · {w.CreatedAt?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)}{(w.Active ? string.Empty : " · " + l.GetString(StringKeys.ModInactive))}");
            if (w.Active && moderation.CanWarn)
            {
                var revoke = new HyperlinkButton { Content = l.GetString(StringKeys.ModRevoke), Padding = new Thickness(0) };
                revoke.Click += async (_, _) =>
                {
                    if (await moderation.RunAsync(() => moderation.Api.RevokeWarningAsync(w.Id)))
                    {
                        revoke.IsEnabled = false;
                        moderation.Show(l.GetString(StringKeys.ModRevoked), InfoBarSeverity.Success);
                    }
                };
                row.Children.Add(revoke);
            }

            panel.Children.Add(row);
        }

        // Sperren
        panel.Children.Add(Section(l.GetString(StringKeys.ModBans)));
        foreach (var b in member.Bans)
        {
            var row = Row($"{l.GetString("Mod_Scope_" + b.Scope)} · {b.Reason}{(b.Active ? string.Empty : " · " + l.GetString(StringKeys.ModInactive))}");
            if (b.Active && moderation.CanBan)
            {
                var lift = new HyperlinkButton { Content = l.GetString(StringKeys.BanLiftAction), Padding = new Thickness(0) };
                lift.Click += async (_, _) =>
                {
                    if (await moderation.RunAsync(() => moderation.Api.LiftBanAsync(b.Id)))
                    {
                        lift.IsEnabled = false;
                        moderation.Show(l.GetString(StringKeys.ModLifted), InfoBarSeverity.Success);
                    }
                };
                row.Children.Add(lift);
            }

            panel.Children.Add(row);
        }

        // Neue Maßnahme
        var reason = new TextBox { Header = l.GetString(StringKeys.ModReason), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var points = new NumberBox { Header = l.GetString(StringKeys.ModPoints), Value = 1, Minimum = 0, Maximum = 100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var days = new NumberBox { Header = l.GetString(StringKeys.ModDays), Value = 0, Minimum = 0, Maximum = 3650, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var scope = new ComboBox
        {
            Header = l.GetString(StringKeys.ModScope),
            ItemsSource = new[] { "forum", "site", "gameserver" }.Select(s => new SettingsOption(s, l.GetString("Mod_Scope_" + s))).ToList(),
            SelectedIndex = 0,
        };
        var isPublic = new CheckBox { Content = l.GetString(StringKeys.ModPublic) };
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };

        panel.Children.Add(Section(l.GetString(StringKeys.ModNewAction)));
        panel.Children.Add(reason);
        var numbers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        if (moderation.CanWarn)
        {
            numbers.Children.Add(points);
        }

        numbers.Children.Add(days);
        panel.Children.Add(numbers);
        if (moderation.CanBan)
        {
            panel.Children.Add(scope);
            panel.Children.Add(isPublic);
        }

        panel.Children.Add(error);
        dialog.Content = new ScrollViewer { Content = panel, MaxHeight = 560 };

        if (moderation.CanWarn)
        {
            dialog.PrimaryButtonText = l.GetString(StringKeys.ModWarn);
        }

        if (moderation.CanBan)
        {
            dialog.SecondaryButtonText = l.GetString(StringKeys.ModBan);
        }

        async Task Act(ContentDialogButtonClickEventArgs args, bool ban)
        {
            var deferral = args.GetDeferral();
            try
            {
                if (string.IsNullOrWhiteSpace(reason.Text))
                {
                    args.Cancel = true;
                    error.Message = l.GetString(StringKeys.ModReasonMissing);
                    error.IsOpen = true;
                    return;
                }

                var dayCount = double.IsNaN(days.Value) ? 0 : (int)days.Value;
                var ok = ban
                    ? await moderation.RunAsync(() => moderation.Api.BanAsync(userId, ((SettingsOption)scope.SelectedItem).Value, reason.Text, dayCount, isPublic.IsChecked == true))
                    : await moderation.RunAsync(() => moderation.Api.WarnAsync(userId, reason.Text, double.IsNaN(points.Value) ? 1 : (int)points.Value, dayCount, reportId));
                if (!ok)
                {
                    args.Cancel = true;
                    error.Message = moderation.FeedbackMessage;
                    error.IsOpen = true;
                    return;
                }

                moderation.Show(l.GetString(ban ? StringKeys.ModBanned : StringKeys.ModWarned), InfoBarSeverity.Success);
            }
            finally
            {
                deferral.Complete();
            }
        }

        dialog.PrimaryButtonClick += async (_, args) => await Act(args, ban: false);
        dialog.SecondaryButtonClick += async (_, args) => await Act(args, ban: true);
        await dialog.ShowAsync();
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text.ToUpper(CultureInfo.CurrentCulture),
        Style = (Style)Application.Current.Resources["RoGSectionTextStyle"],
    };

    private static StackPanel Row(string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 340, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
}
