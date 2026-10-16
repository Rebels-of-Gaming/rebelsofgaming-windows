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

/// <summary>Bewerbungen für die Squad-Leitung: Liste nach Status, Detail mit allem für die Entscheidung.</summary>
public sealed partial class ApplicationsAdminViewModel(AppServices services, Action<string, InfoBarSeverity> show) : ObservableObject
{
    private readonly ApplicationAdminService _admin = new(services.Api.Client, services.Api.Http);
    private string? _cursor;

    public bool CanView => services.CurrentUser is { } me && (me.CanAnywhere("applications.view") || me.CanAnywhere("applications.review")) && me.HasScope("applications:read");

    public bool CanReview => services.CurrentUser is { } me && me.CanAnywhere("applications.review") && me.HasScope("applications:write");

    public IReadOnlyList<SettingsOption> StatusOptions { get; } = new[] { "open", "submitted", "trial", "voting", "accepted", "rejected" }
        .Select(s => new SettingsOption(s, services.Localizer.GetString("AppAdmin_Status_" + s))).ToList();

    public IReadOnlyList<SettingsOption> DecisionOptions { get; } = ApplicationAdminService.Decisions
        .Select(d => new SettingsOption(d, services.Localizer.GetString("AppAdmin_Decision_" + d))).ToList();

    public ObservableCollection<ApplicationRowViewModel> Items { get; } = [];

    public ObservableCollection<AdminTaskViewModel> Tasks { get; } = [];

    [ObservableProperty]
    public partial SettingsOption? SelectedStatus { get; set; }

    [ObservableProperty]
    public partial string? Search { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial ApplicationRowViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial ApplicationDetail? Detail { get; set; }

    [ObservableProperty]
    public partial bool HasDetail { get; set; }

    [ObservableProperty]
    public partial string Header { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AnswersText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChecksText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ActivityText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string VoteText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TimelineText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MessagesText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewTask { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool MessageInternal { get; set; } = true;

    [ObservableProperty]
    public partial SettingsOption? SelectedDecision { get; set; }

    /// <summary>Rückfrage zur Entscheidung (Nachricht an den Bewerber, Tage bei „verlängern“) – setzt die Seite.</summary>
    public Func<string, bool, Task<(bool Ok, string? Message, int? Days)>>? AskDecision { get; set; }

    partial void OnSelectedStatusChanged(SettingsOption? value) => _ = LoadAsync(reset: true);

    partial void OnSelectedChanged(ApplicationRowViewModel? value)
    {
        if (value is not null)
        {
            _ = OpenAsync(value.Id);
        }
    }

    public async Task LoadAsync(bool reset)
    {
        if (!CanView)
        {
            return;
        }

        SelectedStatus ??= StatusOptions[0];
        if (reset)
        {
            _cursor = null;
            Items.Clear();
        }

        await RunAsync(async () =>
        {
            var page = await _admin.ListAsync(SelectedStatus.Value, Search, _cursor);
            foreach (var a in page.Items)
            {
                Items.Add(new ApplicationRowViewModel(a, services.Localizer));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        });
    }

    [RelayCommand]
    private Task MoreAsync() => LoadAsync(reset: false);

    [RelayCommand]
    private Task SearchNowAsync() => LoadAsync(reset: true);

    private async Task OpenAsync(string id)
    {
        await RunAsync(async () =>
        {
            var d = await _admin.DetailAsync(id);
            Detail = d;
            HasDetail = true;
            var l = services.Localizer;
            Header = $"{d.Applicant} → {d.Squad} · {l.GetString("AppAdmin_Status_" + d.Status)} · {string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.VotesAge), d.Age)}";
            AnswersText = string.Join("\n\n", d.Answers.Select(a => $"{a.Question}\n→ {a.Answer}"))
                + (d.Referral is { } r ? $"\n\n{l.GetString(StringKeys.ApplyReferral)}: {r}" : string.Empty);
            ChecksText = string.Join("\n", d.BanCheck.Select(c => $"{(c.Status == "ok" ? "✓" : "⚠")} {c.Message}"));
            ActivityText = d.ActivityMinutes.Count == 0
                ? l.GetString(StringKeys.AppAdminNoActivity)
                : string.Join(" · ", d.ActivityMinutes.Select(a => $"{l.GetString("AppAdmin_Source_" + a.Key)} {a.Value / 60.0:0.0} h"));
            VoteText = d.Vote is { } v
                ? string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.AppAdminVote), v.Yes, v.No, v.Abstain, v.Participation, v.QuorumPercent,
                    l.GetString("AppAdmin_Result_" + v.Result)) + (v.Missing.Count > 0 ? "\n" + string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.AppAdminMissing), string.Join(", ", v.Missing)) : string.Empty)
                : string.Empty;
            TimelineText = string.Join("\n", d.Timeline.Select(t => $"• {t.Label}" + (t.At is { } at ? $" ({at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)})" : string.Empty)));
            MessagesText = string.Join("\n\n", d.Messages.Select(m => $"{(m.Internal ? "🔒 " : string.Empty)}{m.Author}: {m.Body}"));
            Tasks.Clear();
            foreach (var t in d.Tasks)
            {
                Tasks.Add(new AdminTaskViewModel(t, this));
            }
        });
    }

    [RelayCommand]
    private async Task AddTaskAsync()
    {
        if (Detail is { } d && !string.IsNullOrWhiteSpace(NewTask) && await RunAsync(() => _admin.AddTaskAsync(d.Id, NewTask)))
        {
            NewTask = string.Empty;
            await OpenAsync(d.Id);
        }
    }

    internal async Task ToggleTaskAsync(AdminTaskViewModel task, bool done)
    {
        if (Detail is { } d)
        {
            await RunAsync(() => _admin.SetTaskDoneAsync(d.Id, task.Id, done));
        }
    }

    internal async Task DeleteTaskAsync(AdminTaskViewModel task)
    {
        if (Detail is { } d && await RunAsync(() => _admin.DeleteTaskAsync(d.Id, task.Id)))
        {
            Tasks.Remove(task);
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (Detail is { } d && !string.IsNullOrWhiteSpace(NewMessage) && await RunAsync(() => _admin.MessageAsync(d.Id, NewMessage, MessageInternal)))
        {
            NewMessage = string.Empty;
            await OpenAsync(d.Id);
        }
    }

    [RelayCommand]
    private async Task RemindAsync()
    {
        if (Detail is { } d && await RunAsync(() => _admin.RemindAsync(d.Id)))
        {
            show(services.Localizer.GetString(StringKeys.AppAdminReminded), InfoBarSeverity.Success);
        }
    }

    [RelayCommand]
    private async Task DecideAsync()
    {
        if (Detail is not { } d || SelectedDecision is not { } decision || AskDecision is null)
        {
            return;
        }

        var (ok, message, days) = await AskDecision(decision.Label, decision.Value == "extend");
        if (ok && await RunAsync(() => _admin.DecideAsync(d.Id, decision.Value, message, days)))
        {
            show(services.Localizer.GetString(StringKeys.AppAdminDecided), InfoBarSeverity.Success);
            await OpenAsync(d.Id);
            await LoadAsync(reset: true);
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
            show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            show(services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }

        return false;
    }
}

public sealed class ApplicationRowViewModel(ApplicationSummary summary, ILocalizer localizer)
{
    public string Id => summary.Id;

    public string Title { get; } = $"{summary.Applicant} → {summary.Squad}";

    public string Meta { get; } = string.Join(" · ", new[]
    {
        localizer.GetString("AppAdmin_Status_" + summary.Status),
        summary.SubmittedAt?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
        summary.Participation is { } p ? $"{p} %" : null,
    }.Where(x => !string.IsNullOrEmpty(x)));
}

public sealed partial class AdminTaskViewModel(ApplicationTask task, ApplicationsAdminViewModel owner) : ObservableObject
{
    public string Id => task.Id;

    public string Title => task.Title;

    [ObservableProperty]
    public partial bool Done { get; set; } = task.Done;

    partial void OnDoneChanged(bool value) => _ = owner.ToggleTaskAsync(this, value);

    [RelayCommand]
    private Task DeleteAsync() => owner.DeleteTaskAsync(this);
}
