using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Notifications;
using RoG.Core.Localization;
using RoG.Core.Navigation;

namespace RoG.App.ViewModels;

/// <summary>Glocke: Postfach mit „nur ungelesen“, gelesen markieren, Ziel öffnen; neue Einträge kommen live dazu.</summary>
public sealed partial class NotificationsViewModel : LoadableViewModel
{
    private readonly NotificationService _notifications;
    private string? _cursor;

    public NotificationsViewModel(AppServices services)
        : base(services)
    {
        _notifications = new NotificationService(services.Api.Client);
    }

    public ObservableCollection<NotificationRowViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool UnreadOnly { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsGuest { get; set; }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IsGuest = await Services.Tokens.GetAsync(cancellationToken) is null;
        if (IsGuest)
        {
            Items.Clear();
            HasMore = false;
            return;
        }

        var page = await _notifications.ListAsync(null, UnreadOnly, cancellationToken);
        Items.Clear();
        foreach (var n in page.Items)
        {
            Items.Add(new NotificationRowViewModel(n, this));
        }

        _cursor = page.NextCursor;
        HasMore = page.HasMore;
    }

    /// <summary>Live-Eintrag vorn anstellen (Seite ist offen).</summary>
    public void Prepend(NotificationItem item)
    {
        if (Items.All(i => i.Item.Id != item.Id))
        {
            Items.Insert(0, new NotificationRowViewModel(item, this));
            DataChanged();
        }
    }

    partial void OnUnreadOnlyChanged(bool value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_cursor is null)
        {
            return;
        }

        try
        {
            var page = await _notifications.ListAsync(_cursor, UnreadOnly);
            foreach (var n in page.Items)
            {
                Items.Add(new NotificationRowViewModel(n, this));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // „Mehr laden“ erneut
        }
    }

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        try
        {
            await _notifications.MarkAllReadAsync();
            foreach (var row in Items)
            {
                row.MarkReadLocally();
            }

            Services.Realtime.UnreadNotifications = 0;
            await ToastService.ClearAsync();
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            ErrorMessage = null; // Liste bleibt; Zähler korrigiert der nächste Abgleich
        }
    }

    [RelayCommand]
    private void SignIn() => Services.RequestSignIn();

    /// <summary>Eintrag öffnen: gelesen markieren, Ziel in der App (sonst Browser).</summary>
    internal async Task OpenAsync(NotificationRowViewModel row)
    {
        if (row.IsUnread)
        {
            row.MarkReadLocally();
            Services.Realtime.UnreadNotifications = Math.Max(0, Services.Realtime.UnreadNotifications - 1);
            try
            {
                await _notifications.MarkReadAsync(row.Item.Id);
            }
            catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
            {
                // nicht schlimm – der nächste Abgleich korrigiert den Zähler
            }
        }

        if (row.Item.Url is { } url && (AppRoute.FromWebUrl(url, Services.Environment) is not { } route || !Services.Navigate(route)))
        {
            await Services.Browser.OpenAsync(url);
        }
    }
}

public sealed partial class NotificationRowViewModel(NotificationItem item, NotificationsViewModel owner) : ObservableObject
{
    public NotificationItem Item => item;

    public string Title => item.Title;

    public string Body => item.Body ?? string.Empty;

    public bool HasBody => item.Body is not null;

    public string Time { get; } = item.CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DotBrush), nameof(TitleWeight))]
    public partial bool IsUnread { get; set; } = item.IsUnread;

    public Brush DotBrush => (Brush)Application.Current.Resources[IsUnread ? "RoGRedBrush" : "RoGBgBrush"];

    public Windows.UI.Text.FontWeight TitleWeight => IsUnread ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;

    public void MarkReadLocally() => IsUnread = false;

    [RelayCommand]
    private Task OpenAsync() => owner.OpenAsync(this);
}
