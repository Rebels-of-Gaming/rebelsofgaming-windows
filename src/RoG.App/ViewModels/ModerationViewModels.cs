using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Moderation;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Moderation in der Verwaltung: Meldungen, aktive Sperren, Mod-Log, Wortfilter. Jeder Teil nur mit Recht + Scope
/// aus <c>/v1/me</c>; Mitglieder verwarnen/sperren über <see cref="OpenMember"/> (Dialog der Seite).
/// </summary>
public sealed partial class ModerationViewModel(AppServices services) : ObservableObject
{
    private readonly ModerationService _moderation = new(services.Api.Client);
    private string? _reportsCursor;
    private int _loadGeneration;

    public AppServices Services => services;

    public ModerationService Api => _moderation;

    public bool CanReports => Allowed("moderation.reports", "moderation:read");

    public bool CanBan => Allowed("moderation.ban", "moderation:write");

    public bool CanWarn => Allowed("moderation.warn", "moderation:write");

    public bool CanLog => Allowed("moderation.log", "moderation:read");

    public bool CanManage => Allowed("moderation.manage", "moderation:manage");

    public bool Any => CanReports || CanLog || CanManage;

    public IReadOnlyList<SettingsOption> StatusOptions { get; } =
    [
        new("open", services.Localizer.GetString(StringKeys.ModStatusOpen)),
        new("resolved", services.Localizer.GetString(StringKeys.ModStatusResolved)),
        new("dismissed", services.Localizer.GetString(StringKeys.ModStatusDismissed)),
    ];

    public IReadOnlyList<SettingsOption> WordActions { get; } =
    [
        new("replace", services.Localizer.GetString(StringKeys.ModWordReplace)),
        new("block", services.Localizer.GetString(StringKeys.ModWordBlock)),
        new("review", services.Localizer.GetString(StringKeys.ModWordReview)),
    ];

    /// <summary>Filter nach Grund (<c>filter[reason]</c>): alle oder nur Kinderschutz.</summary>
    public IReadOnlyList<SettingsOption> ReasonOptions { get; } =
    [
        new(string.Empty, services.Localizer.GetString(StringKeys.ModReasonAll)),
        new(ModerationService.ChildSafety, services.Localizer.GetString("Report_Reason_" + ModerationService.ChildSafety)),
    ];

    [ObservableProperty]
    public partial SettingsOption? SelectedReason { get; set; }

    /// <summary>Filter nach Herkunft (<c>filter[source]</c>): alle, Website oder Minecraft-Netzwerk (v0.38.0).</summary>
    public IReadOnlyList<SettingsOption> SourceOptions { get; } =
    [
        new(string.Empty, services.Localizer.GetString(StringKeys.ModSourceAll)),
        .. ModerationService.ReportSources.Select(s => new SettingsOption(s, services.Localizer.GetString("Mod_Source_" + s))),
    ];

    [ObservableProperty]
    public partial SettingsOption? SelectedSource { get; set; }

    partial void OnSelectedSourceChanged(SettingsOption? value) => _ = LoadReportsAsync(reset: true);

    public ObservableCollection<ReportItemViewModel> Reports { get; } = [];

    public ObservableCollection<ModBanItemViewModel> Bans { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    public ObservableCollection<WordItemViewModel> Words { get; } = [];

    [ObservableProperty]
    public partial SettingsOption? SelectedStatus { get; set; }

    [ObservableProperty]
    public partial bool HasMoreReports { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string NewPattern { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SettingsOption? NewAction { get; set; }

    [ObservableProperty]
    public partial string NewReplacement { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    /// <summary>Mitglied moderieren (Dialog) – setzt die Seite.</summary>
    public Func<string, string?, Task>? OpenMember { get; set; }

    /// <summary>Rückfrage – setzt die Seite.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);

    partial void OnSelectedStatusChanged(SettingsOption? value) => _ = LoadReportsAsync(reset: true);

    partial void OnSelectedReasonChanged(SettingsOption? value) => _ = LoadReportsAsync(reset: true);

    public async Task LoadReportsAsync(bool reset)
    {
        if (!CanReports)
        {
            return;
        }

        if (SelectedStatus is null)
        {
            SelectedStatus = StatusOptions[0]; // lädt über OnSelectedStatusChanged – sonst liefe das Laden doppelt
            return;
        }

        var generation = reset ? ++_loadGeneration : _loadGeneration;
        if (reset)
        {
            _reportsCursor = null;
            Reports.Clear();
        }

        await RunAsync(async () =>
        {
            var page = await _moderation.ReportsAsync(
                SelectedStatus.Value,
                _reportsCursor,
                SelectedReason?.Value is { Length: > 0 } reason ? reason : null,
                SelectedSource?.Value is { Length: > 0 } source ? source : null);
            if (generation != _loadGeneration)
            {
                return; // inzwischen neu gefiltert
            }

            foreach (var r in page.Items)
            {
                Reports.Add(new ReportItemViewModel(r, this, services.Localizer));
            }

            _reportsCursor = page.NextCursor;
            HasMoreReports = page.NextCursor is not null;
        });
    }

    [RelayCommand]
    private Task MoreReportsAsync() => LoadReportsAsync(reset: false);

    [RelayCommand]
    private Task ReloadReportsAsync() => LoadReportsAsync(reset: true);

    internal async Task SetReportStatusAsync(ReportItemViewModel item, string status)
    {
        if (await RunAsync(() => _moderation.UpdateReportAsync(item.Id, status, item.Resolution)))
        {
            Reports.Remove(item);
            Show(L(StringKeys.ModReportUpdated), InfoBarSeverity.Success);
        }
    }

    public async Task LoadBansAsync()
    {
        if (!CanReports)
        {
            return;
        }

        await RunAsync(async () =>
        {
            Bans.Clear();
            string? cursor = null;
            do
            {
                var page = await _moderation.BansAsync(cursor);
                foreach (var b in page.Items)
                {
                    Bans.Add(new ModBanItemViewModel(b, this, services.Localizer));
                }

                cursor = page.NextCursor;
            }
            while (cursor is not null && Bans.Count < 300);
        });
    }

    internal async Task LiftAsync(ModBanItemViewModel item)
    {
        if (await Confirm(L(StringKeys.ModLiftTitle), item.Title) && await RunAsync(() => _moderation.LiftBanAsync(item.Id)))
        {
            Bans.Remove(item);
            Show(L(StringKeys.ModLifted), InfoBarSeverity.Success);
        }
    }

    public async Task LoadLogAsync()
    {
        if (!CanLog)
        {
            return;
        }

        await RunAsync(async () =>
        {
            Log.Clear();
            var page = await _moderation.LogAsync();
            foreach (var e in page.Items)
            {
                Log.Add(string.Join(" · ", new[]
                {
                    e.CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    e.Actor,
                    e.Action,
                    e.Target,
                }.Where(s => !string.IsNullOrEmpty(s))));
            }
        });
    }

    public async Task LoadWordsAsync()
    {
        if (!CanManage)
        {
            return;
        }

        NewAction ??= WordActions[0];
        await RunAsync(async () =>
        {
            Words.Clear();
            foreach (var w in await _moderation.WordsAsync())
            {
                Words.Add(new WordItemViewModel(w, this, services.Localizer));
            }
        });
    }

    [RelayCommand]
    private async Task AddWordAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPattern))
        {
            return;
        }

        if (await RunAsync(() => _moderation.AddWordAsync(NewPattern, NewAction?.Value ?? "replace", NewReplacement)))
        {
            NewPattern = NewReplacement = string.Empty;
            await LoadWordsAsync();
        }
    }

    internal async Task DeleteWordAsync(WordItemViewModel item)
    {
        if (await Confirm(L(StringKeys.ModWordDeleteTitle), item.Pattern) && await RunAsync(() => _moderation.DeleteWordAsync(item.Id)))
        {
            Words.Remove(item);
        }
    }

    internal Task OpenMemberAsync(string userId, string? reportId) => OpenMember?.Invoke(userId, reportId) ?? Task.CompletedTask;

    public async Task<bool> RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
            return true;
        }
        catch (ApiProblemException ex)
        {
            Show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or ArgumentException)
        {
            Show(services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }

    public void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }

    private string L(string key) => services.Localizer.GetString(key);

    private bool Allowed(string permission, string scope) =>
        services.CurrentUser is { } me && me.CanAnywhere(permission) && me.HasScope(scope);
}

public sealed partial class ReportItemViewModel(ModReport report, ModerationViewModel owner, ILocalizer localizer) : ObservableObject
{
    public string Id => report.Id;

    public string Title { get; } = $"{Label(localizer, "Mod_Type_", report.Type)} · {Label(localizer, "Report_Reason_", report.Reason)}";

    private static string Label(ILocalizer localizer, string prefix, string value)
    {
        var text = localizer.GetString(prefix + value);
        return text.StartsWith(prefix, StringComparison.Ordinal) ? value : text;
    }

    public string Meta { get; } = string.Join(" · ", new[]
    {
        report.CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        report.Reporter is null ? null : string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.ModReportedBy), report.Reporter),
        report.Target is null ? null : string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.ModAgainst), report.Target.Name),
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool IsChildSafety => report.IsChildSafety;

    /// <summary>Spiel-Meldung: „Notch (Java) gemeldet von jeb_ · RoG Network · lobby“.</summary>
    public string GameText { get; } = report.Game is { } g
        ? string.Join(" · ", new[]
        {
            g.Reported is { } d ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.ModGameReported), Player(localizer, d)) : null,
            g.Reporter is { } r ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.ModReportedBy), Player(localizer, r)) : null,
            g.ServerName,
            g.NetworkServer,
        }.Where(x => !string.IsNullOrEmpty(x)))
        : string.Empty;

    public bool IsGame => report.FromMinecraft;

    private static string Player(ILocalizer localizer, ModGamePlayer p) =>
        p.Edition is { } e ? $"{p.Name} ({MinecraftAccountsViewModel.EditionText(localizer, e, null)})" : p.Name;

    public string Excerpt { get; } = report.Excerpt ?? string.Empty;

    public bool HasExcerpt => !string.IsNullOrEmpty(report.Excerpt);

    public string Comment { get; } = report.Comment ?? string.Empty;

    public bool HasComment => !string.IsNullOrEmpty(report.Comment);

    public bool IsOpen => report.Status == "open";

    public bool CanModerateTarget => report.Target is not null && (owner.CanWarn || owner.CanBan);

    [ObservableProperty]
    public partial string Resolution { get; set; } = report.Resolution ?? string.Empty;

    [RelayCommand]
    private Task ResolveAsync() => owner.SetReportStatusAsync(this, "resolved");

    [RelayCommand]
    private Task DismissAsync() => owner.SetReportStatusAsync(this, "dismissed");

    [RelayCommand]
    private Task ModerateAsync() => report.Target is { } t ? owner.OpenMemberAsync(t.Id, report.Id) : Task.CompletedTask;

    public bool HasUrl => report.Url is not null;

    /// <summary>Gemeldeten Inhalt öffnen – in der App, wenn es eine Seite dafür gibt, sonst im Browser.</summary>
    [RelayCommand]
    private Task OpenContentAsync()
    {
        if (report.Url is not { } url)
        {
            return Task.CompletedTask;
        }

        var services = App.Services;
        return RoG.Core.Navigation.AppRoute.FromWebUrl(url, services.Environment) is { } route && services.Navigate(route)
            ? Task.CompletedTask
            : services.Browser.OpenAsync(url);
    }
}

public sealed partial class ModBanItemViewModel(ModBan ban, ModerationViewModel owner, ILocalizer localizer)
{
    public string Id => ban.Id;

    public string Title { get; } = $"{ban.User?.Name ?? "—"} · {localizer.GetString("Mod_Scope_" + ban.Scope)}";

    public string Meta { get; } = string.Join(" · ", new[]
    {
        ban.Reason,
        ban.EndsAt is { } e
            ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.BanUntil), e.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : localizer.GetString(StringKeys.BanPermanent),
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool CanLift => owner.CanBan;

    [RelayCommand]
    private Task LiftAsync() => owner.LiftAsync(this);

    [RelayCommand]
    private Task OpenAsync() => ban.User is { } u ? owner.OpenMemberAsync(u.Id, null) : Task.CompletedTask;
}

public sealed partial class WordItemViewModel(WordFilter word, ModerationViewModel owner, ILocalizer localizer)
{
    public string Id => word.Id;

    public string Pattern => word.Pattern;

    public string Meta { get; } = localizer.GetString("Mod_Word_" + word.Action) + (word.Replacement is { } r ? $" → {r}" : string.Empty);

    [RelayCommand]
    private Task DeleteAsync() => owner.DeleteWordAsync(this);
}
