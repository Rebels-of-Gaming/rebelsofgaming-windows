using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Applications;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Eigene Bewerbungen (Konto-Seite): Status mit Schritt, Timeline, Trial-Aufgaben, Nachrichten, zurückziehen.</summary>
public sealed partial class MyApplicationsViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly ApplicationService _applications = new(services.Api.Client, services.Api.Http);

    public ObservableCollection<MyApplicationViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var list = await _applications.MineAsync(cancellationToken);
        Items.Clear();
        foreach (var a in list)
        {
            Items.Add(new MyApplicationViewModel(a, this, Services.Localizer));
        }
    }

    internal async Task SendAsync(MyApplicationViewModel item, string text)
    {
        try
        {
            await _applications.SendMessageAsync(item.Id, text);
            Show(Services.Localizer.GetString(StringKeys.ApplyMessageSent), InfoBarSeverity.Success);
            await LoadAsync(silent: true);
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

    internal async Task WithdrawAsync(MyApplicationViewModel item)
    {
        try
        {
            await _applications.WithdrawAsync(item.Id);
            await LoadAsync(silent: true);
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

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }
}

public sealed partial class MyApplicationViewModel(MyApplication app, MyApplicationsViewModel owner, ILocalizer localizer) : ObservableObject
{
    public string Id => app.Id;

    public string Title { get; } = $"{app.SquadName} – {app.StatusLabel}";

    /// <summary>Schritt 0–4 als Prozent für den Fortschrittsbalken.</summary>
    public double Progress { get; } = Math.Clamp(app.Step, 0, 4) * 25;

    public string Details { get; } = string.Join("\n", new[]
    {
        app.SubmittedAt is { } s ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.ApplySubmittedAt), s.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)) : null,
        app.TrialEndsAt is { } t ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.ApplyTrialEnds), t.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)) : null,
        app.VoteParticipation is { } v ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.ApplyVoteParticipation), v) : null,
        app.DecisionMessage,
    }.Where(x => !string.IsNullOrEmpty(x)));

    public string Timeline { get; } = string.Join("\n", app.Timeline.Select(t => $"• {t.Label}" + (t.At is { } at ? $" ({at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)})" : string.Empty)));

    public string Tasks { get; } = string.Join("\n", app.Tasks.Select(t => (t.Done ? "☑ " : "☐ ") + t.Title));

    public bool HasTasks => app.Tasks.Count > 0;

    public string Messages { get; } = string.Join("\n\n", app.Messages.Select(m => $"{(m.FromSquad ? m.Author ?? app.SquadName : localizer.GetString(StringKeys.ApplyYou))}: {m.Body}"));

    public bool HasMessages => app.Messages.Count > 0;

    public bool IsOpen => app.IsOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Draft { get; set; } = string.Empty;

    private bool CanSend() => !string.IsNullOrWhiteSpace(Draft);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Draft;
        Draft = string.Empty;
        await owner.SendAsync(this, text);
    }

    [RelayCommand]
    private Task WithdrawAsync() => owner.WithdrawAsync(this);
}
