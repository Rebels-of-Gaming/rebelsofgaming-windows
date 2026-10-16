using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Features.Applications;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Offene Votes über Bewerbungen: Antworten, Trial-Aufgaben, Beteiligung; Ja/Nein/Enthaltung.</summary>
public sealed partial class VotesViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly VoteService _votes = new(services.Api.Client, services.Api.Http);

    public ObservableCollection<VoteItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (await Services.Tokens.GetAsync(cancellationToken) is null)
        {
            Items.Clear();
            return;
        }

        var votes = await _votes.OpenAsync(cancellationToken);
        Items.Clear();
        foreach (var v in votes)
        {
            Items.Add(new VoteItemViewModel(v, this, Services.Localizer));
        }
    }

    internal async Task CastAsync(VoteItemViewModel item, VoteChoice choice)
    {
        try
        {
            await _votes.CastAsync(item.ApplicationId, choice);
            Show(Services.Localizer.GetString(StringKeys.VotesCast), InfoBarSeverity.Success);
            await LoadAsync(silent: true);
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (GeneratedApiException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }
}

public sealed partial class VoteItemViewModel(OpenVote vote, VotesViewModel owner, ILocalizer localizer)
{
    public string ApplicationId => vote.ApplicationId;

    public string Title { get; } = $"{vote.ApplicantName} → {vote.SquadName}";

    public string Meta { get; } = string.Join(" · ", new[]
    {
        string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.VotesAge), vote.Age),
        string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.VotesParticipation), vote.Participation, vote.QuorumPercent),
        vote.EndsAt is { } e ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.VotesEnds), e.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string Answers { get; } = string.Join("\n", vote.Answers.Select(a => $"{a.Question}\n→ {a.Answer}"));

    public string Tasks { get; } = string.Join("\n", vote.Tasks.Select(t => (t.Done ? "☑ " : "☐ ") + t.Title));

    public bool HasTasks => vote.Tasks.Count > 0;

    public string MyVote { get; } = vote.MyVote switch
    {
        "yes" => localizer.GetString(StringKeys.VotesMineYes),
        "no" => localizer.GetString(StringKeys.VotesMineNo),
        "abstain" => localizer.GetString(StringKeys.VotesMineAbstain),
        _ => string.Empty,
    };

    public bool HasMyVote => vote.MyVote is not null;

    [RelayCommand]
    private Task YesAsync() => owner.CastAsync(this, VoteChoice.Yes);

    [RelayCommand]
    private Task NoAsync() => owner.CastAsync(this, VoteChoice.No);

    [RelayCommand]
    private Task AbstainAsync() => owner.CastAsync(this, VoteChoice.Abstain);
}
