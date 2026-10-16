using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RoG.App.Services;
using RoG.Core.Features.Members;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Mitglieder: Suche, „nur online“, Team-Ansicht; Profil daneben.</summary>
public sealed partial class MembersViewModel : LoadableViewModel
{
    private readonly MemberService _members;
    private string? _cursor;
    private int _searchVersion;

    public MembersViewModel(AppServices services)
        : base(services)
    {
        _members = new MemberService(services.Api.Client);
        Detail = new MemberDetailViewModel(services, _members);
    }

    public ObservableCollection<MemberListItem> Items { get; } = [];

    public MemberDetailViewModel Detail { get; }

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    public partial bool OnlineOnly { get; set; }

    /// <summary>true = Team-Seite (Gruppen mit Zuständigkeiten), false = alle Mitglieder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFilters))]
    public partial bool TeamMode { get; set; }

    public bool ShowFilters => !TeamMode;

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial MemberListItem? Selected { get; set; }

    /// <summary>Detail per Link geöffnet, ohne dass der Eintrag in der geladenen Liste steht.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial bool DetailForced { get; set; }

    public bool IsDetailOpen => Selected is not null || DetailForced;

    public bool ShowSelectHint => !IsDetailOpen;

    /// <summary>Liste laden und den Eintrag <paramref name="slug"/> öffnen (Suche, Links, später Deep Links).</summary>
    public async Task OpenAsync(string slug)
    {
        await LoadAsync();
        Selected = Items.FirstOrDefault(i => i.Slug == slug);
        if (Selected is null)
        {
            DetailForced = true;
            await Detail.ShowAsync(slug);
        }
    }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var items = new List<MemberListItem>();
        if (TeamMode)
        {
            foreach (var group in await _members.GetTeamAsync(cancellationToken))
            {
                foreach (var m in group.Members)
                {
                    var subtitle = string.Join(" · ", new[] { group.Name, m.Responsibility }.Where(s => !string.IsNullOrEmpty(s)));
                    items.Add(new MemberListItem(m.Slug, m.Name, m.Avatar, subtitle, m.RoleLabel, m.RoleColor, null));
                }
            }

            _cursor = null;
            HasMore = false;
        }
        else
        {
            var page = await _members.ListAsync(SearchText, OnlineOnly, null, cancellationToken);
            items.AddRange(page.Items.Select(Item));
            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        }

        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_cursor is null || TeamMode)
        {
            return;
        }

        try
        {
            var page = await _members.ListAsync(SearchText, OnlineOnly, _cursor);
            foreach (var m in page.Items)
            {
                Items.Add(Item(m));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        }
        catch (Exception ex) when (ex is RoG.Core.Api.ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // erneut versuchen über „Mehr laden“
        }
    }

    private MemberListItem Item(MemberSummary m) =>
        new(m.Slug, m.Name, m.Avatar, m.Activity ?? string.Join(" · ", m.Squads), m.RankLabel, m.RankColor, m.Presence);

    // Suche mit kurzer Verzögerung, damit nicht jeder Tastendruck eine Anfrage auslöst
    partial void OnSearchTextChanged(string? value) => _ = ReloadDelayedAsync();

    partial void OnOnlineOnlyChanged(bool value) => _ = LoadAsync();

    partial void OnTeamModeChanged(bool value) => _ = LoadAsync();

    private async Task ReloadDelayedAsync()
    {
        // Nur die letzte Eingabe innerhalb von 350 ms lädt
        var version = ++_searchVersion;
        await Task.Delay(350);
        if (version == _searchVersion)
        {
            await LoadAsync();
        }
    }

    partial void OnSelectedChanged(MemberListItem? value)
    {
        if (value is not null)
        {
            DetailForced = false;
        }

        if (value is not null)
        {
            _ = Detail.ShowAsync(value.Slug);
        }
    }
}

/// <summary>Zeile in der Mitgliederliste: Avatar mit Status-Punkt, Name, Rang/Rolle, Zeile darunter.</summary>
public sealed class MemberListItem(string slug, string name, Uri? avatar, string? subtitle, string? rank, string? rankColor, Presence? presence)
{
    public string Slug => slug;

    public string Name => name;

    public ImageSource? Avatar { get; } = avatar is null ? null : new BitmapImage(avatar) { DecodePixelWidth = 64 };

    public string Subtitle => subtitle ?? string.Empty;

    public string Rank => rank ?? string.Empty;

    public Brush RankBrush { get; } = AccountViewModel.ColorFromHex(rankColor) is { } c ? new SolidColorBrush(c) : (Brush)Application.Current.Resources["RoGGoldBrush"];

    public bool HasPresence => presence is not null;

    public Brush PresenceBrush { get; } = MemberDetailViewModel.PresenceBrush(presence ?? Presence.Offline);
}

public sealed partial class MemberDetailViewModel(AppServices services, MemberService members) : LoadableViewModel(services)
{
    private string? _slug;
    private string? _id;

    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial ImageSource? Avatar { get; set; }

    [ObservableProperty]
    public partial ImageSource? Banner { get; set; }

    [ObservableProperty]
    public partial bool CanMessage { get; set; }

    [ObservableProperty]
    public partial string? Rank { get; set; }

    [ObservableProperty]
    public partial Brush? RankBrush { get; set; }

    [ObservableProperty]
    public partial Brush? PresenceBrushValue { get; set; }

    [ObservableProperty]
    public partial string? PresenceText { get; set; }

    [ObservableProperty]
    public partial string? Motto { get; set; }

    [ObservableProperty]
    public partial string? Bio { get; set; }

    [ObservableProperty]
    public partial string? Facts { get; set; }

    [ObservableProperty]
    public partial string? Squads { get; set; }

    public ObservableCollection<BadgeViewModel> Badges { get; } = [];

    public ObservableCollection<string> Platforms { get; } = [];

    [ObservableProperty]
    public partial bool HasBadges { get; set; }

    [ObservableProperty]
    public partial bool HasPlatforms { get; set; }

    protected override bool HasData => Name is not null;

    public Task ShowAsync(string slug)
    {
        if (slug == _slug && Name is not null)
        {
            return Task.CompletedTask;
        }

        _slug = slug;
        Name = null;
        return LoadAsync();
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_slug is null)
        {
            return;
        }

        var p = await members.GetAsync(_slug, cancellationToken);
        var s = p.Summary;
        _id = s.Id;
        var me = Services.CurrentUser?.Id ?? (await Services.Tokens.GetAsync(cancellationToken))?.UserId;
        CanMessage = me is not null && me != s.Id;
        Avatar = s.Avatar is null ? null : new BitmapImage(s.Avatar);
        Banner = p.Banner is null ? null : new BitmapImage(p.Banner);
        Rank = s.RankLabel;
        RankBrush = AccountViewModel.ColorFromHex(s.RankColor) is { } c ? new SolidColorBrush(c) : (Brush)Application.Current.Resources["RoGGoldBrush"];
        PresenceBrushValue = PresenceBrush(s.Presence);
        PresenceText = string.Join(" · ", new[] { PresenceLabel(s.Presence, Services.Localizer), s.Activity }.Where(x => !string.IsNullOrEmpty(x)));
        Motto = p.Motto;
        Bio = p.Bio;
        Squads = s.Squads.Count > 0 ? string.Join(" · ", s.Squads) : null;

        var facts = new List<string>();
        if (p.Location is not null)
        {
            facts.Add(p.Location);
        }

        if (p.JoinedAt is { } joined)
        {
            facts.Add(Format(StringKeys.AccountJoined, joined.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)));
        }

        if (s.Presence == Presence.Offline && p.LastSeenAt is { } seen)
        {
            facts.Add(Format(StringKeys.MembersLastSeen, seen.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));
        }

        Facts = string.Join(" · ", facts);

        Badges.Clear();
        foreach (var badge in p.Badges)
        {
            Badges.Add(new BadgeViewModel(badge.Name, badge.Description, badge.Color));
        }

        HasBadges = Badges.Count > 0;

        Platforms.Clear();
        foreach (var (platform, handle) in p.Platforms)
        {
            Platforms.Add($"{platform}: {handle}");
        }

        HasPlatforms = Platforms.Count > 0;
        Name = s.Name;
    }

    /// <summary>1:1-Unterhaltung öffnen bzw. starten und in „Nachrichten“ zeigen.</summary>
    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (_id is null)
        {
            return;
        }

        try
        {
            var conversation = await new RoG.Core.Features.Messages.MessageService(Services.Api.Client).StartDirectAsync(_id);
            Services.Navigate(new RoG.Core.Navigation.AppRoute(RoG.Core.Navigation.AppSection.Messages, conversation));
        }
        catch (RoG.Core.Api.ApiProblemException ex)
        {
            ErrorMessage = Services.Localizer.GetError(ex.Problem.Code);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            ErrorMessage = Services.Localizer.GetError(RoG.Core.Api.ApiProblem.Codes.Unknown);
        }
    }

    [RelayCommand]
    private Task OpenProfileAsync() =>
        _slug is null ? Task.CompletedTask : Services.Browser.OpenAsync(new Uri(Services.Environment.WebBaseUrl, $"u/{Uri.EscapeDataString(_slug)}"));

    internal static Brush PresenceBrush(Presence presence) => (Brush)Application.Current.Resources[presence switch
    {
        Presence.Online => "RoGStatusOnlineBrush",
        Presence.Voice => "RoGStatusVoiceBrush",
        Presence.Server => "RoGStatusServerBrush",
        Presence.Ingame => "RoGStatusInGameBrush",
        _ => "RoGStatusOfflineBrush",
    }];

    internal static string PresenceLabel(Presence presence, ILocalizer localizer) => localizer.GetString(presence switch
    {
        Presence.Online => StringKeys.PresenceOnline,
        Presence.Voice => StringKeys.PresenceVoice,
        Presence.Server => StringKeys.PresenceServer,
        Presence.Ingame => StringKeys.PresenceIngame,
        _ => StringKeys.PresenceOffline,
    });
}

/// <summary>Abzeichen als farbiger Chip.</summary>
public sealed class BadgeViewModel(string name, string? description, string color)
{
    public string Name => name;

    public string Description => description ?? name;

    public Brush ChipBrush { get; } = AccountViewModel.ColorFromHex(color) is { } c ? new SolidColorBrush(c) : (Brush)Application.Current.Resources["RoGGoldBrush"];
}
