using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RoG.App.Services;
using RoG.Core.Features.Voice;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>TeamSpeak-Viewer (Kanäle mit Personen, „Verbinden“ über ts3server://) und Discord-Voice.</summary>
public sealed partial class VoiceViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly VoiceService _voice = new(services.Api.Client);
    private TeamSpeakState? _ts;

    public ObservableCollection<VoiceRow> TeamSpeakRows { get; } = [];

    public ObservableCollection<VoiceRow> DiscordRows { get; } = [];

    [ObservableProperty]
    public partial string? TeamSpeakTitle { get; set; }

    [ObservableProperty]
    public partial string? TeamSpeakStatus { get; set; }

    [ObservableProperty]
    public partial bool CanConnect { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDiscordEmpty))]
    public partial bool HasDiscord { get; set; }

    /// <summary>Geladen, aber niemand im Discord-Voice.</summary>
    public bool ShowDiscordEmpty => !IsLoading && !HasError && !HasDiscord;

    [ObservableProperty]
    public partial bool ShowEmptyChannels { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    protected override bool HasData => _ts is not null || DiscordRows.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var tsTask = _voice.GetTeamSpeakAsync(cancellationToken);
        var discordTask = _voice.GetDiscordAsync(cancellationToken);

        try
        {
            _ts = await tsTask;
            TeamSpeakTitle = _ts.ServerName;
            TeamSpeakStatus = _ts.Online
                ? Format(StringKeys.VoiceTsOnline, _ts.ClientsOnline, _ts.MaxClients)
                : Services.Localizer.GetString(StringKeys.VoiceTsOffline);
            CanConnect = VoiceService.TeamSpeakLink(_ts.Address, null) is not null;
            BuildTeamSpeakRows();
        }
        catch (Exception ex) when (ex is RoG.Core.Api.ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // TeamSpeak nicht eingerichtet oder nicht erreichbar – Discord kann trotzdem da sein
            _ts = null;
            TeamSpeakTitle = "TeamSpeak";
            TeamSpeakStatus = Services.Localizer.GetString(StringKeys.VoiceTsOffline);
            CanConnect = false;
            TeamSpeakRows.Clear();
        }

        DiscordRows.Clear();
        try
        {
            foreach (var channel in await discordTask)
            {
                DiscordRows.Add(VoiceRow.Channel(channel.Name, 0, channel.Private));
                foreach (var (name, avatar) in channel.Members)
                {
                    DiscordRows.Add(VoiceRow.Person(name, 1, null, false, false, avatar));
                }
            }
        }
        catch (Exception ex) when (ex is RoG.Core.Api.ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // Discord-Voice ist optional
        }

        HasDiscord = DiscordRows.Count > 0;
    }

    partial void OnShowEmptyChannelsChanged(bool value) => BuildTeamSpeakRows();

    private void BuildTeamSpeakRows()
    {
        TeamSpeakRows.Clear();
        if (_ts is null)
        {
            return;
        }

        foreach (var channel in _ts.Channels)
        {
            Add(channel, 0);
        }

        DataChanged();
    }

    private void Add(TsChannel channel, int depth)
    {
        if (channel.Spacer || (!ShowEmptyChannels && !channel.HasPeople))
        {
            return;
        }

        TeamSpeakRows.Add(VoiceRow.Channel(channel.Name, depth, false));
        foreach (var client in channel.Clients)
        {
            TeamSpeakRows.Add(VoiceRow.Person(client.Nickname, depth + 1, client.MemberName, client.Away, client.Muted, null));
        }

        foreach (var child in channel.Children)
        {
            Add(child, depth + 1);
        }
    }

    /// <summary>TeamSpeak öffnen und direkt verbinden; Spitzname = RoG-Name. Ohne TeamSpeak: Adresse kopieren.</summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        var nickname = Services.CurrentUser?.Name ?? (await Services.Tokens.GetAsync())?.UserName;
        var link = VoiceService.TeamSpeakLink(_ts?.Address, nickname);
        if (link is null || _ts?.Address is null)
        {
            return;
        }

        if (await Services.Launcher.CanLaunchAsync(link) && await Services.Launcher.LaunchAsync(link))
        {
            Show(Format(StringKeys.VoiceTsLaunched, _ts.ServerName), InfoBarSeverity.Success);
            return;
        }

        var copied = Services.ServerConnector.Copy(_ts.Address);
        Show(Format(copied ? StringKeys.VoiceTsMissing : StringKeys.ServersCopyFailed, _ts.Address), copied ? InfoBarSeverity.Informational : InfoBarSeverity.Error);
    }

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }
}

/// <summary>Zeile im Voice-Baum: Kanal oder Person (eingerückt nach Tiefe).</summary>
public sealed class VoiceRow
{
    private VoiceRow()
    {
    }

    public required string Text { get; init; }

    public string? Detail { get; init; }

    public bool IsChannel { get; init; }

    public bool IsPerson => !IsChannel;

    public Thickness Indent { get; init; }

    public string Glyph { get; init; } = string.Empty;

    public Brush Foreground { get; init; } = (Brush)Application.Current.Resources["RoGInkBrush"];

    public ImageSource? Avatar { get; init; }

    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    public static VoiceRow Channel(string name, int depth, bool locked) => new()
    {
        Text = name,
        IsChannel = true,
        Indent = new Thickness(depth * 20, 6, 0, 2),
        Glyph = locked ? "" : "",
        Foreground = (Brush)Application.Current.Resources["RoGMutedBrush"],
    };

    public static VoiceRow Person(string name, int depth, string? member, bool away, bool muted, Uri? avatar) => new()
    {
        Text = name,
        Detail = member is not null && member != name ? member : null,
        Indent = new Thickness(depth * 20, 2, 0, 2),
        Glyph = muted ? "" : away ? "" : "",
        Foreground = (Brush)Application.Current.Resources[away ? "RoGMutedBrush" : "RoGInkBrush"],
        Avatar = avatar is null ? null : new BitmapImage(avatar) { DecodePixelWidth = 48 },
    };
}
