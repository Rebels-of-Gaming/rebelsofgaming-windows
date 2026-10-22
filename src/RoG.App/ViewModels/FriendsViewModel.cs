using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Friends;
using RoG.Core.Features.Members;
using RoG.Core.Localization;
using RoG.Core.Navigation;

namespace RoG.App.ViewModels;

/// <summary>
/// Freunde (Website v0.38.0, Guide 45): eigene Freundesliste mit Online-Status, offene Anfragen (an mich: annehmen/ablehnen,
/// von mir: zurückziehen), Freund per Name/Profil-Link hinzufügen, entfernen. Die Liste ist privat; Grenzen, Sperren und
/// Blockieren prüft der Server. Ältere Geräte-Tokens haben <c>friends:read</c>/<c>friends:write</c> erst nach neuer Anmeldung.
/// </summary>
public sealed partial class FriendsViewModel : LoadableViewModel
{
    private readonly FriendsService _friends;

    public FriendsViewModel(AppServices services)
        : base(services)
    {
        _friends = new FriendsService(services.Api.Http);
        var me = services.CurrentUser;
        CanRead = me is not null && me.HasScope(FriendsService.ReadScope);
        CanWrite = me is not null && me.HasScope(FriendsService.WriteScope);
        NeedsSignIn = me is not null && !(CanRead && CanWrite);
    }

    public bool CanRead { get; }

    public bool CanWrite { get; }

    /// <summary>Token ohne Freunde-Scopes: Hinweis „einmal ab- und wieder anmelden“.</summary>
    public bool NeedsSignIn { get; }

    public ObservableCollection<FriendItemViewModel> Friends { get; } = [];

    public ObservableCollection<FriendRequestItemViewModel> Incoming { get; } = [];

    public ObservableCollection<FriendRequestItemViewModel> Outgoing { get; } = [];

    [ObservableProperty]
    public partial string AddInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? CountText { get; set; }

    [ObservableProperty]
    public partial bool HasIncoming { get; set; }

    [ObservableProperty]
    public partial bool HasOutgoing { get; set; }

    [ObservableProperty]
    public partial bool HasNoFriends { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    /// <summary>Entfernen/Ablehnen bestätigen lassen (Titel, Text) – setzt die Seite.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);

    protected override bool HasData => CanRead;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!CanRead)
        {
            return;
        }

        var friends = new List<FriendEntry>();
        FriendPage? page = null;
        do
        {
            page = await _friends.FriendsAsync(page?.NextCursor, cancellationToken);
            friends.AddRange(page.Items);
        }
        while (page.HasMore && friends.Count < 500);

        var requests = new List<FriendRequest>();
        Page<FriendRequest>? r = null;
        do
        {
            r = await _friends.RequestsAsync(null, r?.NextCursor, cancellationToken);
            requests.AddRange(r.Items);
        }
        while (r.HasMore && requests.Count < 500);

        Friends.Clear();
        foreach (var f in friends.OrderBy(f => f.Status is null or Presence.Offline).ThenBy(f => f.User.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Friends.Add(new FriendItemViewModel(f, this, Services.Localizer));
        }

        Incoming.Clear();
        Outgoing.Clear();
        foreach (var request in requests)
        {
            (request.Incoming ? Incoming : Outgoing).Add(new FriendRequestItemViewModel(request, this, Services.Localizer));
        }

        HasIncoming = Incoming.Count > 0;
        HasOutgoing = Outgoing.Count > 0;
        HasNoFriends = Friends.Count == 0;
        CountText = page.Limit is { } limit
            ? Format(StringKeys.FriendsCount, page.Total ?? Friends.Count, limit)
            : null;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (FriendsService.NormalizeUser(AddInput) is not { } user)
        {
            Show(L(StringKeys.FriendsAddInvalid), InfoBarSeverity.Warning);
            return;
        }

        await RunAsync(async () =>
        {
            var result = await _friends.SendAsync(user);
            AddInput = string.Empty;
            Show(Format(result.IsAccepted ? StringKeys.FriendsNowFriends : StringKeys.FriendsRequestSent, result.User.Name), InfoBarSeverity.Success);
        });
    }

    internal Task AcceptAsync(FriendRequestItemViewModel item) => RunAsync(async () =>
    {
        var friend = await _friends.AcceptAsync(item.Id);
        Show(Format(StringKeys.FriendsNowFriends, friend.User.Name), InfoBarSeverity.Success);
    });

    internal async Task DeclineAsync(FriendRequestItemViewModel item)
    {
        if (await Confirm(L(StringKeys.FriendsDeclineTitle), Format(StringKeys.FriendsDeclineText, item.Name)))
        {
            await RunAsync(async () =>
            {
                await _friends.DeclineAsync(item.Id);
                Show(Format(StringKeys.FriendsDeclined, item.Name), InfoBarSeverity.Informational);
            });
        }
    }

    internal Task CancelAsync(FriendRequestItemViewModel item) => RunAsync(async () =>
    {
        await _friends.CancelAsync(item.Id);
        Show(Format(StringKeys.FriendsCancelled, item.Name), InfoBarSeverity.Informational);
    });

    internal async Task RemoveAsync(FriendItemViewModel item)
    {
        if (await Confirm(L(StringKeys.FriendsRemoveTitle), Format(StringKeys.FriendsRemoveText, item.Name)))
        {
            await RunAsync(async () =>
            {
                await _friends.RemoveAsync(item.UserId);
                Show(Format(StringKeys.FriendsRemoved, item.Name), InfoBarSeverity.Informational);
            });
        }
    }

    internal void OpenProfile(string slug) => Services.Navigate(new AppRoute(AppSection.Members, slug));

    internal async Task MessageAsync(FriendItemViewModel item)
    {
        try
        {
            var conversation = await new RoG.Core.Features.Messages.MessageService(Services.Api.Client).StartDirectAsync(item.UserId);
            Services.Navigate(new AppRoute(AppSection.Messages, conversation));
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    /// <summary>Aktion ausführen und danach still neu laden (die Liste kommt immer vom Server).</summary>
    private async Task RunAsync(Func<Task> action)
    {
        if (!CanWrite || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or ArgumentException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync(silent: true);
    }

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }

    private string L(string key) => Services.Localizer.GetString(key);
}

public sealed partial class FriendItemViewModel(FriendEntry friend, FriendsViewModel owner, ILocalizer localizer)
{
    public string UserId => friend.User.Id;

    public string Name => friend.User.Name;

    public ImageSource? Avatar { get; } = friend.User.AvatarUrl is { } url ? new BitmapImage(url) : null;

    public bool HasPresence => friend.Status is not null;

    public Brush PresenceBrush { get; } = MemberDetailViewModel.PresenceBrush(friend.Status ?? Presence.Offline);

    public string Meta { get; } = string.Join(" · ", new[]
    {
        friend.Status is { } s ? MemberDetailViewModel.PresenceLabel(s, localizer) : null,
        friend.Since is { } since ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.FriendsSince), since.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)) : null,
    }.Where(x => !string.IsNullOrEmpty(x)));

    public bool CanWrite => owner.CanWrite;

    [RelayCommand]
    private void OpenProfile() => owner.OpenProfile(friend.User.Slug);

    [RelayCommand]
    private Task MessageAsync() => owner.MessageAsync(this);

    [RelayCommand]
    private Task RemoveAsync() => owner.RemoveAsync(this);
}

public sealed partial class FriendRequestItemViewModel(FriendRequest request, FriendsViewModel owner, ILocalizer localizer)
{
    public string Id => request.Id;

    public string Name => request.User.Name;

    public ImageSource? Avatar { get; } = request.User.AvatarUrl is { } url ? new BitmapImage(url) : null;

    public string Meta { get; } = request.CreatedAt is { } at
        ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(request.Incoming ? StringKeys.FriendsReceivedAt : StringKeys.FriendsSentAt), at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
        : string.Empty;

    public bool CanWrite => owner.CanWrite;

    [RelayCommand]
    private void OpenProfile() => owner.OpenProfile(request.User.Slug);

    [RelayCommand]
    private Task AcceptAsync() => owner.AcceptAsync(this);

    [RelayCommand]
    private Task DeclineAsync() => owner.DeclineAsync(this);

    [RelayCommand]
    private Task CancelAsync() => owner.CancelAsync(this);
}
