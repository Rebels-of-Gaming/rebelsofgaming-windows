using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Servers;
using RoG.Core.Features.Squads;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Squads: Übersicht und Squad-Seite (Vorstellung, Line-up, Links, Server mit „Verbinden“).</summary>
public sealed partial class SquadsViewModel : LoadableViewModel
{
    private readonly SquadService _squads;

    public SquadsViewModel(AppServices services)
        : base(services)
    {
        _squads = new SquadService(services.Api.Client);
        Detail = new SquadDetailViewModel(services, _squads);
        Games = new GamesViewModel(services);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShowContent))
            {
                OnPropertyChanged(nameof(ShowSquads));
            }
        };
    }

    public ObservableCollection<SquadItemViewModel> Items { get; } = [];

    /// <summary>Umschalter „Spiele“ statt der Squad-Liste.</summary>
    public GamesViewModel Games { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSquads))]
    public partial bool GamesMode { get; set; }

    public bool ShowSquads => ShowContent && !GamesMode;

    partial void OnGamesModeChanged(bool value)
    {
        if (value && Games.Items.Count == 0)
        {
            _ = Games.LoadAsync();
        }
    }

    public SquadDetailViewModel Detail { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial SquadItemViewModel? Selected { get; set; }

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
        var squads = await _squads.ListAsync(cancellationToken);
        var selected = Selected?.Slug;
        Items.Clear();
        foreach (var squad in squads)
        {
            Items.Add(new SquadItemViewModel(squad, Services.Localizer));
        }

        if (selected is not null)
        {
            Selected = Items.FirstOrDefault(i => i.Slug == selected);
        }
    }

    partial void OnSelectedChanged(SquadItemViewModel? value)
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

public sealed class SquadItemViewModel(SquadSummary squad, ILocalizer localizer)
{
    public string Slug => squad.Slug;

    public string Name => squad.Name;

    public string Subtitle { get; } = string.Join(" · ", new[] { squad.GameName, squad.ClanTag, squad.Tagline }.Where(s => !string.IsNullOrEmpty(s)));

    public ImageSource? Logo { get; } = squad.Logo is null ? null : new BitmapImage(squad.Logo) { DecodePixelWidth = 96 };

    public string RecruitingText { get; } = SquadDetailViewModel.TextForRecruiting(squad.Recruiting, localizer);

    public Brush RecruitingBrush { get; } = SquadDetailViewModel.BrushForRecruiting(squad.Recruiting);

    public bool HasRecruiting => squad.Recruiting != Recruiting.Unknown;
}

public sealed partial class SquadDetailViewModel(AppServices services, SquadService squads) : LoadableViewModel(services)
{
    private readonly ServerService _servers = new(services.Api.Client);
    private string? _slug;

    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial string? Subtitle { get; set; }

    [ObservableProperty]
    public partial ImageSource? Header { get; set; }

    [ObservableProperty]
    public partial ImageSource? Logo { get; set; }

    [ObservableProperty]
    public partial string? RecruitingText { get; set; }

    [ObservableProperty]
    public partial Brush? RecruitingBrush { get; set; }

    [ObservableProperty]
    public partial string? Description { get; set; }

    [ObservableProperty]
    public partial string? TrainingTimes { get; set; }

    [ObservableProperty]
    public partial string? ClanInfo { get; set; }

    [ObservableProperty]
    public partial string? MembersHeader { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    public ObservableCollection<MemberRowViewModel> Members { get; } = [];

    public ObservableCollection<LinkViewModel> Links { get; } = [];

    public ObservableCollection<ServerItemViewModel> Servers { get; } = [];

    [ObservableProperty]
    public partial bool HasMembers { get; set; }

    [ObservableProperty]
    public partial bool HasLinks { get; set; }

    [ObservableProperty]
    public partial bool HasServers { get; set; }

    /// <summary>Bewerben möglich: angemeldet und der Squad sucht (offen oder Warteliste).</summary>
    [ObservableProperty]
    public partial bool CanApply { get; set; }

    public string? Slug => _slug;

    protected override bool HasData => Name is not null;

    public Task ShowAsync(string slug)
    {
        if (slug == _slug && Name is not null)
        {
            return Task.CompletedTask;
        }

        _slug = slug;
        Name = null;
        StatsText = string.Empty;
        _statsLoaded = false;
        return LoadAsync();
    }

    private bool _statsLoaded;

    /// <summary>Squad-Wertung der letzten 30 Tage (Top 10, Waffen) – beim ersten Aufklappen geladen.</summary>
    [ObservableProperty]
    public partial string StatsText { get; set; } = string.Empty;

    public async Task LoadStatsAsync()
    {
        if (_statsLoaded || _slug is null)
        {
            return;
        }

        _statsLoaded = true;
        var l = Services.Localizer;
        try
        {
            var stats = await new SquadExtrasService(Services.Api.Client).StatsAsync(_slug);
            if (stats.Members.Count == 0)
            {
                StatsText = l.GetString(StringKeys.SquadStatsEmpty);
                return;
            }

            var lines = stats.Members.Take(10).Select((m, i) => string.Format(CultureInfo.CurrentCulture, "{0}. {1}{2} – {3} K · {4} D · K/D {5} · {6} R",
                i + 1, m.Name, m.Substitute ? " (E)" : string.Empty, m.Kills, m.Deaths, m.Kd?.ToString("0.00", CultureInfo.CurrentCulture) ?? "–", m.Rounds));
            var weapons = stats.Weapons.Count > 0 ? "\n\n" + string.Join(" · ", stats.Weapons.Select(w => $"{w.Weapon} {w.Kills}")) : string.Empty;
            StatsText = (stats.GameName is { } g ? g + "\n" : string.Empty) + string.Join("\n", lines) + weapons;
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            _statsLoaded = false;
            StatsText = ex is ApiProblemException p ? l.GetError(p.Problem.Code) : l.GetError(ApiProblem.Codes.Unknown);
        }
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_slug is null)
        {
            return;
        }

        var detailTask = squads.GetAsync(_slug, cancellationToken);
        var membersTask = squads.GetMembersAsync(_slug, cancellationToken);
        var serversTask = _servers.GetAllAsync(_slug, cancellationToken);
        var detail = await detailTask;
        var s = detail.Summary;

        Subtitle = string.Join(" · ", new[] { s.GameName, s.LeaderName is null ? null : Format(StringKeys.SquadsLeader, s.LeaderName) }.Where(x => !string.IsNullOrEmpty(x)));
        Header = s.Header is null ? null : new BitmapImage(s.Header);
        Logo = s.Logo is null ? null : new BitmapImage(s.Logo);
        RecruitingText = TextForRecruiting(s.Recruiting, Services.Localizer);
        RecruitingBrush = BrushForRecruiting(s.Recruiting);
        CanApply = s.Recruiting is Recruiting.Open or Recruiting.Waitlist && await Services.Tokens.GetAsync(cancellationToken) is not null;
        Description = detail.Description;
        TrainingTimes = detail.TrainingTimes;
        ClanInfo = detail.ClanName is null && s.ClanTag is null
            ? null
            : string.Join(" · ", new[] { s.ClanTag, detail.ClanName, detail.ClanLevel is { } level ? Format(StringKeys.SquadsClanLevel, level) : null, detail.ClanRequirements }.Where(x => !string.IsNullOrEmpty(x)));

        Links.Clear();
        foreach (var link in detail.Links)
        {
            Links.Add(new LinkViewModel(link.Label, link.Url, link.Code, Services));
        }

        HasLinks = Links.Count > 0;

        Members.Clear();
        try
        {
            foreach (var m in await membersTask)
            {
                Members.Add(new MemberRowViewModel(m.Name, m.Slug, m.Avatar, m.Substitute ? Format(StringKeys.SquadsSubstitute, m.RoleLabel ?? string.Empty).Trim() : m.RoleLabel, m.RoleColor));
            }
        }
        catch (Exception ex) when (ex is RoG.Core.Api.ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // Line-up ist Beiwerk – die Squad-Seite bleibt sichtbar
        }

        HasMembers = Members.Count > 0;
        MembersHeader = Format(StringKeys.SquadsMembers, Members.Count);

        Servers.Clear();
        try
        {
            foreach (var server in await serversTask)
            {
                Servers.Add(new ServerItemViewModel(server, Services.Localizer, ConnectAsync, Copy));
            }
        }
        catch (Exception ex) when (ex is RoG.Core.Api.ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // ohne Server weiter
        }

        HasServers = Servers.Count > 0;
        Name = s.Name;
    }

    private async Task ConnectAsync(ServerItemViewModel item)
    {
        var message = ConnectMessage.Describe(await Services.ServerConnector.ConnectAsync(item.Server), item.Name, Services.Localizer);
        Show(message.Text, ServersViewModel.ToSeverity(message.Tone));
    }

    private void Copy(ServerItemViewModel item)
    {
        var ok = Services.ServerConnector.Copy(item.Address);
        Show(Format(ok ? StringKeys.ServersCopied : StringKeys.ServersCopyFailed, item.Address), ok ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }

    internal static string TextForRecruiting(Recruiting recruiting, ILocalizer localizer) => recruiting switch
    {
        Recruiting.Open => localizer.GetString(StringKeys.SquadsRecruitingOpen),
        Recruiting.Waitlist => localizer.GetString(StringKeys.SquadsRecruitingWaitlist),
        Recruiting.Full => localizer.GetString(StringKeys.SquadsRecruitingFull),
        _ => string.Empty,
    };

    internal static Brush BrushForRecruiting(Recruiting recruiting) => (Brush)Application.Current.Resources[recruiting switch
    {
        Recruiting.Open => "RoGGreenBrush",
        Recruiting.Waitlist => "RoGGoldBrush",
        _ => "RoGMutedBrush",
    }];
}

/// <summary>Person in einer Liste (Line-up, Team, Mitglieder): Avatar, Name, Rolle in Rollenfarbe.</summary>
public sealed class MemberRowViewModel(string name, string slug, Uri? avatar, string? role, string? roleColor)
{
    public string Name => name;

    public string Slug => slug;

    public ImageSource? Avatar { get; } = avatar is null ? null : new BitmapImage(avatar) { DecodePixelWidth = 64 };

    public string Role => role ?? string.Empty;

    public bool HasRole => !string.IsNullOrEmpty(role);

    public Brush RoleBrush { get; } = AccountViewModel.ColorFromHex(roleColor) is { } c ? new SolidColorBrush(c) : (Brush)Application.Current.Resources["RoGMutedBrush"];
}

/// <summary>Link-Knopf (Einladung, Ref-Link): öffnen und kopieren.</summary>
public sealed partial class LinkViewModel(string label, Uri url, string? code, AppServices services)
{
    public string Label => label;

    public string Url => url.ToString();

    public string? Code => code;

    public bool HasCode => !string.IsNullOrEmpty(code);

    [RelayCommand]
    private Task OpenAsync() => services.Browser.OpenAsync(url);

    [RelayCommand]
    private void Copy()
    {
        try
        {
            services.Clipboard.SetText(code ?? url.ToString());
        }
#pragma warning disable CA1031 // Zwischenablage kann gesperrt sein – dann eben nicht
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
