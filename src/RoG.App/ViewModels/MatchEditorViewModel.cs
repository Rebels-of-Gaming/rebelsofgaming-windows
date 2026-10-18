using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Matches;
using RoG.Core.Features.Squads;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Match eintragen/bearbeiten (eigenes Fenster): Squad (nur mit <c>matches.manage</c>), Art, Status, Gegner, Zeitpunkt,
/// Maps als Zeilen („supply 2:1“), Gesamtergebnis, Line-up aus dem Squad, Links, Bericht, veröffentlichen, Anhänge.
/// </summary>
public sealed partial class MatchEditorViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MatchAdminService _admin;
    private readonly SquadService _squads;
    private IReadOnlyList<MatchPlayerDraft> _players = [];

    public MatchEditorViewModel(AppServices services)
    {
        _services = services;
        _admin = new MatchAdminService(services.Api.Http);
        _squads = new SquadService(services.Api.Client);
        Social = new SocialStatusViewModel(services);
        TypeOptions = MatchAdminService.Types.Select(t => new SettingsOption(t, L("MatchEdit_Type_" + t))).ToList();
        StatusOptions = MatchAdminService.Statuses.Select(t => new SettingsOption(t, L("MatchEdit_Status_" + t))).ToList();
        SelectedType = TypeOptions[0];
        SelectedStatus = StatusOptions[1];
    }

    public bool Changed { get; private set; }

    public IReadOnlyList<SettingsOption> TypeOptions { get; }

    public IReadOnlyList<SettingsOption> StatusOptions { get; }

    public ObservableCollection<SettingsOption> SquadOptions { get; } = [];

    public ObservableCollection<MatchPlayerItem> Players { get; } = [];

    public ObservableCollection<MatchAttachmentItem> Attachments { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaved), nameof(Heading))]
    public partial string? Id { get; set; }

    public bool IsSaved => Id is not null;

    /// <summary>Karte „Social Media“ (nur mit Recht <c>social.publish</c>).</summary>
    public SocialStatusViewModel Social { get; }

    partial void OnIdChanged(string? value) => _ = Social.ShowAsync(RoG.Core.Features.Social.SocialSources.Match, value);

    public string Heading => L(Id is null ? StringKeys.MatchEditNew : StringKeys.MatchEditEdit);

    [ObservableProperty]
    public partial SettingsOption? SelectedSquad { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedType { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedStatus { get; set; }

    [ObservableProperty]
    public partial string Opponent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OpponentTag { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OpponentUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTimeOffset? PlayedDate { get; set; } = DateTimeOffset.Now.Date;

    [ObservableProperty]
    public partial TimeSpan PlayedTime { get; set; } = new(20, 0, 0);

    [ObservableProperty]
    public partial string MapsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double ScoreOwn { get; set; } = double.NaN;

    [ObservableProperty]
    public partial double ScoreOpponent { get; set; } = double.NaN;

    [ObservableProperty]
    public partial string LinksText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Report { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool? Published { get; set; } = false;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    public Func<string, string, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);

    private string L(string key) => _services.Localizer.GetString(key);

    public async Task StartAsync(string? matchId)
    {
        await RunAsync(async () =>
        {
            var me = _services.CurrentUser;
            SquadOptions.Clear();
            foreach (var s in (await _squads.ListAsync()).Where(s => MatchAdminService.CanManage(me, s.Id)))
            {
                SquadOptions.Add(new SettingsOption(s.Id, s.Name));
            }
        });
        if (matchId is not null)
        {
            await RunAsync(async () => Fill(await _admin.GetAsync(matchId)));
        }
        else
        {
            SelectedSquad = SquadOptions.FirstOrDefault();
        }
    }

    private void Fill(MatchDraft draft)
    {
        Id = draft.Id;
        _players = draft.Players ?? [];
        SelectedSquad = SquadOptions.FirstOrDefault(s => s.Value == draft.SquadId) ?? SelectedSquad;
        SelectedType = TypeOptions.FirstOrDefault(t => t.Value == draft.Type) ?? TypeOptions[0];
        SelectedStatus = StatusOptions.FirstOrDefault(t => t.Value == draft.Status) ?? StatusOptions[1];
        Opponent = draft.Opponent;
        OpponentTag = draft.OpponentTag ?? string.Empty;
        OpponentUrl = draft.OpponentUrl ?? string.Empty;
        var local = draft.PlayedAt.ToLocalTime();
        PlayedDate = new DateTimeOffset(local.Date);
        PlayedTime = local.TimeOfDay;
        MapsText = MatchAdminService.FormatMaps(draft.Maps);
        ScoreOwn = draft.ScoreOwn ?? double.NaN;
        ScoreOpponent = draft.ScoreOpponent ?? double.NaN;
        LinksText = MatchAdminService.FormatLinks(draft.Links ?? []);
        Report = draft.Report ?? string.Empty;
        Published = draft.Published;
        Attachments.Clear();
        foreach (var a in draft.Attachments ?? [])
        {
            Attachments.Add(new MatchAttachmentItem(a, this));
        }

        MarkPlayers();
    }

    partial void OnSelectedSquadChanged(SettingsOption? value)
    {
        if (value is not null)
        {
            _ = LoadPlayersAsync(value.Value);
        }
    }

    /// <summary>Line-up zur Auswahl: Mitglieder des Squads, beim Bearbeiten mit den bisherigen Spielern.</summary>
    private async Task LoadPlayersAsync(string squadId) => await RunAsync(async () =>
    {
        var squad = (await _squads.ListAsync()).FirstOrDefault(s => s.Id == squadId);
        Players.Clear();
        if (squad is null)
        {
            return;
        }

        foreach (var m in await _squads.GetMembersAsync(squad.Slug))
        {
            Players.Add(new MatchPlayerItem(m.Id, m.Name) { Substitute = m.Substitute });
        }

        MarkPlayers();
    });

    private void MarkPlayers()
    {
        foreach (var p in Players)
        {
            if (_players.FirstOrDefault(x => x.UserId == p.UserId) is { } saved)
            {
                p.Selected = true;
                p.Substitute = saved.Slot == "substitute";
                p.Position = saved.Position ?? string.Empty;
            }
        }
    }

    private MatchDraft Current()
    {
        var date = (PlayedDate ?? DateTimeOffset.Now).Date + PlayedTime;
        var players = Players.Where(p => p.Selected == true)
            .Select(p => new MatchPlayerDraft(p.UserId, p.Substitute == true ? "substitute" : "starter", p.Position)).ToList();
        return new MatchDraft(
            Id,
            SelectedSquad?.Value ?? string.Empty,
            SelectedType?.Value ?? "clanwar",
            SelectedStatus?.Value ?? "finished",
            Opponent,
            new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date)),
            MatchAdminService.ParseMaps(MapsText),
            OpponentTag,
            OpponentUrl,
            double.IsNaN(ScoreOwn) ? null : (int)ScoreOwn,
            double.IsNaN(ScoreOpponent) ? null : (int)ScoreOpponent,
            Published == true,
            Report,
            Players.Count > 0 ? players : null,
            MatchAdminService.ParseLinks(LinksText));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var draft = Current();
        var errors = MatchAdminService.Validate(draft);
        if (errors.Count > 0)
        {
            Show(string.Join(" ", errors.Select(e => L("MatchEdit_Error_" + e))), InfoBarSeverity.Warning);
            return;
        }

        string? id = null;
        if (await RunAsync(async () => id = await _admin.SaveAsync(draft, _services.Language)))
        {
            Changed = true;
            var before = Id;
            Id = string.IsNullOrEmpty(id) ? Id : id;
            if (Id == before)
            {
                // Ergebnis veröffentlicht → Website legt ggf. einen Entwurf an
                _ = Social.ShowAsync(RoG.Core.Features.Social.SocialSources.Match, Id);
            }

            Show(L(StringKeys.MatchEditSaved), InfoBarSeverity.Success);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Id is { } id && await Confirm(L(StringKeys.MatchEditDeleteTitle), Opponent) && await RunAsync(() => _admin.DeleteAsync(id)))
        {
            Changed = true;
            Id = null;
            Attachments.Clear();
            Show(L(StringKeys.MatchEditDeleted), InfoBarSeverity.Success);
        }
    }

    public async Task AttachAsync(IReadOnlyList<(Stream Content, string Name)> files)
    {
        if (Id is not { } id || files.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            foreach (var chunk in files.Chunk(5))
            {
                if (!await RunAsync(() => _admin.AttachAsync(id, chunk)))
                {
                    return;
                }
            }

            Changed = true;
            await RunAsync(async () => Fill(await _admin.GetAsync(id)));
            Show(L(StringKeys.MatchEditAttached), InfoBarSeverity.Success);
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task RemoveAttachmentAsync(MatchAttachmentItem item)
    {
        if (Id is { } id && await Confirm(L(StringKeys.MatchEditRemoveAttachment), item.Name)
            && await RunAsync(() => _admin.RemoveAttachmentAsync(id, item.Attachment.Id)))
        {
            Changed = true;
            Attachments.Remove(item);
        }
    }

    private async Task<bool> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (ApiProblemException ex)
        {
            Show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : _services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            Show(_services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }

        return false;
    }

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }
}

/// <summary>Mitglied in der Line-up-Auswahl (dabei, Ersatz, Position).</summary>
public sealed partial class MatchPlayerItem(string userId, string name) : ObservableObject
{
    public string UserId => userId;

    public string Name => name;

    [ObservableProperty]
    public partial bool? Selected { get; set; } = false;

    [ObservableProperty]
    public partial bool? Substitute { get; set; } = false;

    [ObservableProperty]
    public partial string Position { get; set; } = string.Empty;
}

public sealed partial class MatchAttachmentItem(MatchAttachment attachment, MatchEditorViewModel owner)
{
    public MatchAttachment Attachment => attachment;

    public string Name => attachment.Name;

    [RelayCommand]
    private Task RemoveAsync() => owner.RemoveAttachmentAsync(this);
}
