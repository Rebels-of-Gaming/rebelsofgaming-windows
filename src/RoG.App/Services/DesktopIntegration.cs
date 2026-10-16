using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using RoG.Core.Features.Notifications;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace RoG.App.Services;

/// <summary>Zahl am Taskleisten-Symbol (ungelesene Benachrichtigungen + Unterhaltungen); 0 entfernt das Badge.</summary>
public static class BadgeService
{
    public static void Set(int count)
    {
        try
        {
            var updater = BadgeUpdateManager.CreateBadgeUpdaterForApplication();
            if (count <= 0)
            {
                updater.Clear();
                return;
            }

            var xml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeNumber);
            ((XmlElement)xml.SelectSingleNode("/badge")).SetAttribute("value", Math.Min(count, 99).ToString(System.Globalization.CultureInfo.InvariantCulture));
            updater.Update(new BadgeNotification(xml));
        }
#pragma warning disable CA1031 // Badge ist Beiwerk (z. B. ohne Paketidentität beim Debuggen) – nie die App stören
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}

/// <summary>
/// Windows-Benachrichtigungen (App SDK <c>AppNotificationManager</c>). Inhalt kommt aus dem Postfach-Eintrag der API
/// und wird lokal erzeugt – er geht nicht über Microsoft-Server. Klick → <see cref="Invoked"/> mit dem Ziel-Link.
/// </summary>
public static class ToastService
{
    private const string UrlArgument = "url";
    private static bool s_registered;

    /// <summary>Klick auf einen Toast: Ziel-Adresse (Website-Link) oder <c>null</c> (nur App öffnen).</summary>
    public static event EventHandler<string?>? Invoked;

    /// <summary>Einmal beim Start, vor jeder anderen Nutzung (Microsoft-Vorgabe: vor dem ersten Fenster).</summary>
    public static void Register()
    {
        if (s_registered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, args) =>
                Invoked?.Invoke(null, args.Arguments.TryGetValue(UrlArgument, out var url) ? url : null);
            AppNotificationManager.Default.Register();
            s_registered = true;
        }
#pragma warning disable CA1031 // ohne Benachrichtigungen weiter (z. B. von Richtlinie gesperrt)
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    public static void Unregister()
    {
        if (s_registered)
        {
            AppNotificationManager.Default.Unregister();
            s_registered = false;
        }
    }

    public static void Show(NotificationItem item)
    {
        if (!s_registered || !AppNotificationManager.IsSupported())
        {
            return;
        }

        try
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("id", item.Id)
                .AddText(item.Title, new AppNotificationTextProperties().SetMaxLines(2));
            if (item.Body is not null)
            {
                builder.AddText(item.Body, new AppNotificationTextProperties().SetMaxLines(3));
            }

            if (item.Url is not null)
            {
                builder.AddArgument(UrlArgument, item.Url.ToString());
            }

            var notification = builder.BuildNotification();
            notification.Group = item.Type ?? "rog";
            AppNotificationManager.Default.Show(notification);
        }
#pragma warning disable CA1031 // Toast ist Beiwerk
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>Einfacher Hinweis ohne Ziel (z. B. „läuft im Infobereich weiter“).</summary>
    public static void ShowInfo(string title, string text)
    {
        if (!s_registered || !AppNotificationManager.IsSupported())
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.Show(new AppNotificationBuilder().AddText(title).AddText(text).BuildNotification());
        }
#pragma warning disable CA1031 // Hinweis ist Beiwerk
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>Gezeigte Toasts entfernen (nach „alle gelesen“).</summary>
    public static async Task ClearAsync()
    {
        if (!s_registered)
        {
            return;
        }

        try
        {
            await AppNotificationManager.Default.RemoveAllAsync();
        }
#pragma warning disable CA1031
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>Argumente einer Toast-Aktivierung beim Kaltstart (App war geschlossen) → Ziel-Link.</summary>
    public static string? UrlFrom(AppNotificationActivatedEventArgs args) =>
        args?.Arguments is { } a && a.TryGetValue(UrlArgument, out var url) ? url : null;
}

/// <summary>Sprungliste (Rechtsklick auf das Taskleisten-Symbol): Einträge starten die App mit einem <c>rog:</c>-Link.</summary>
public static class JumpListService
{
    public static async Task UpdateAsync(RoG.Core.Localization.ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        try
        {
            if (!Windows.UI.StartScreen.JumpList.IsSupported())
            {
                return;
            }

            var list = await Windows.UI.StartScreen.JumpList.LoadCurrentAsync();
            list.SystemGroupKind = Windows.UI.StartScreen.JumpListSystemGroupKind.None;
            list.Items.Clear();
            foreach (var (link, key) in new[]
            {
                ("rog://messages", RoG.Core.Localization.StringKeys.NavMessages),
                ("rog://notifications", RoG.Core.Localization.StringKeys.NavNotifications),
                ("rog://events", RoG.Core.Localization.StringKeys.NavCalendar),
                ("rog://servers", RoG.Core.Localization.StringKeys.NavServers),
                ("rog://news", RoG.Core.Localization.StringKeys.NavNews),
            })
            {
                var item = Windows.UI.StartScreen.JumpListItem.CreateWithArguments(link, localizer.GetString(key));
                item.GroupName = localizer.GetString(RoG.Core.Localization.StringKeys.JumpListGroup);
                item.Logo = new Uri("ms-appx:///Assets/Square44x44Logo.targetsize-24_altform-unplated.png");
                list.Items.Add(item);
            }

            await list.SaveAsync();
        }
#pragma warning disable CA1031 // Sprungliste ist Beiwerk
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
