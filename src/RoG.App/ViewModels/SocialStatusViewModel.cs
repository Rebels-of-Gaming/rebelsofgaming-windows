using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Social;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Karte „Social Media“ an einer Quelle (News, Termin, Clanwar, Galerie-Album): Zustand, Kanäle mit Link/Fehler,
/// erneut senden, neu vorbereiten. Sichtbar nur mit Recht <c>social.publish</c> und Scope <c>social:read</c>.
/// </summary>
public sealed partial class SocialStatusViewModel(AppServices services) : ObservableObject
{
    private readonly SocialSourceService _social = new(services.Api.Http);
    private string? _source;
    private string? _id;
    private SocialSourceStatus? _status;
    private int _generation;

    /// <summary>Rückfrage (Titel, Text) → bestätigt; setzt die Ansicht, die die Karte zeigt.</summary>
    public Func<string, string, Task<bool>>? Confirm { get; set; }

    public bool CanRead => services.CurrentUser is { } me && me.Can(SocialSourceService.Permission) && me.HasScope(SocialSourceService.ReadScope);

    public bool CanWrite => CanRead && services.CurrentUser!.HasScope(SocialSourceService.WriteScope);

    public ObservableCollection<SocialTargetViewModel> Targets { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    public partial bool HasSource { get; set; }

    public bool IsShown => HasSource && CanRead;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StateText { get; set; }

    [ObservableProperty]
    public partial string? DetailText { get; set; }

    [ObservableProperty]
    public partial bool ShowRetryFailed { get; set; }

    [ObservableProperty]
    public partial bool ShowRedraft { get; set; }

    [ObservableProperty]
    public partial string? Feedback { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    /// <summary>Quelle setzen (null = noch nicht gespeichert → Karte aus) und Status laden.</summary>
    public Task ShowAsync(string source, string? id)
    {
        _source = source;
        _id = id;
        HasSource = id is not null;
        Targets.Clear();
        _status = null;
        StateText = null;
        DetailText = null;
        ShowRetryFailed = false;
        ShowRedraft = false;
        IsFeedbackOpen = false;
        return IsShown ? RefreshAsync() : Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_source is null || _id is null || !CanRead)
        {
            return;
        }

        var generation = ++_generation;
        await RunAsync(async () =>
        {
            var status = await _social.GetAsync(_source, _id);
            if (generation == _generation)
            {
                Apply(status);
            }
        }, success: null);
    }

    private void Apply(SocialSourceStatus status)
    {
        _status = status;
        var l = services.Localizer;
        StateText = l.GetString("Social_State_" + status.State) is var t && !t.StartsWith("Social_State_", StringComparison.Ordinal) ? t : status.State;
        DetailText = !status.AnyChannel ? l.GetString(StringKeys.SocialNoChannels)
            : !status.Eligible && status.PostId is null ? l.GetString(StringKeys.SocialNotEligible)
            : status.ScheduledAt is { } at && status.State == "scheduled"
                ? string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.SocialScheduledAt), at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                : null;
        Targets.Clear();
        foreach (var target in status.Targets)
        {
            Targets.Add(new SocialTargetViewModel(target, this, l, CanWrite));
        }

        ShowRetryFailed = CanWrite && status.CanRetryFailed;
        ShowRedraft = CanWrite && status.CanRedraft;
    }

    [RelayCommand]
    private Task RetryFailedAsync() => _status?.PostId is not { } post ? Task.CompletedTask : RunAsync(async () =>
    {
        var result = await _social.RetryFailedAsync(post);
        await ReloadQuietAsync();
        var l = services.Localizer;
        var text = string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.SocialRetryStarted), result.Count);
        if (result.Unusable.Count > 0)
        {
            text += " " + string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.SocialUnusable),
                string.Join(", ", result.Unusable.Select(u => $"{u.Name} ({UnusableText(u.Reason)})")));
        }

        Show(text, result.Unusable.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
    }, success: null);

    internal Task RetryTargetAsync(SocialTargetViewModel target) => _status?.PostId is not { } post ? Task.CompletedTask : RunAsync(async () =>
    {
        await _social.RetryTargetAsync(post, target.Id);
        await ReloadQuietAsync();
    }, success: StringKeys.SocialRetryOne);

    /// <summary>Neu vorbereiten: neuer Entwurf ohne die Kanäle, auf denen es schon steht; sonst bewusst erneut posten.</summary>
    [RelayCommand]
    private async Task RedraftAsync()
    {
        if (_status is not { } status || _source is null || _id is null)
        {
            return;
        }

        var l = services.Localizer;
        var repost = false;
        if (status.State == "published")
        {
            // Alles schon gepostet: nur als bewusst neuer Beitrag
            if (Confirm is null || !await Confirm(l.GetString(StringKeys.SocialRepostTitle), l.GetString(StringKeys.SocialRepostText)))
            {
                return;
            }

            repost = true;
        }

        await RunAsync(async () =>
        {
            await _social.RedraftAsync(_source, _id, repost);
            await ReloadQuietAsync();
        }, success: StringKeys.SocialRedrafted);
    }

    /// <summary>Entwürfe freigeben, Kanäle verwalten: Social-Verwaltung der Website.</summary>
    public bool HasAdminLink => services.Environment.AdminBaseUrl is not null;

    [RelayCommand]
    private Task OpenInAdminAsync() => services.Environment.AdminBaseUrl is { } admin ? services.Browser.OpenAsync(new Uri(admin, "social")) : Task.CompletedTask;

    private async Task ReloadQuietAsync()
    {
        if (_source is not null && _id is not null)
        {
            Apply(await _social.GetAsync(_source, _id));
        }
    }

    internal string UnusableText(string reason) =>
        services.Localizer.GetString("Social_Unusable_" + reason) is var t && !t.StartsWith("Social_Unusable_", StringComparison.Ordinal) ? t : reason;

    internal Task OpenAsync(Uri url) => services.Browser.OpenAsync(url);

    private async Task RunAsync(Func<Task> action, string? success)
    {
        IsBusy = true;
        try
        {
            await action();
            if (success is not null)
            {
                Show(services.Localizer.GetString(success), InfoBarSeverity.Success);
            }
        }
        catch (ApiProblemException ex)
        {
            Show(services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(string text, InfoBarSeverity severity)
    {
        Feedback = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = false;
        IsFeedbackOpen = true;
    }
}

public sealed partial class SocialTargetViewModel(SocialTarget target, SocialStatusViewModel owner, ILocalizer localizer, bool canWrite)
{
    public string Id => target.Id;

    public string Name { get; } = target.ProviderLabel is { } p && p != target.ChannelName ? $"{target.ChannelName} · {p}" : target.ChannelName;

    public string StatusText { get; } = string.Join(" · ", new[]
    {
        Label(localizer, "Social_Target_", target.Status),
        target.SkipReason is { } r ? Label(localizer, "Social_Skip_", r) : null,
        target.PublishedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string Error => target.Error ?? string.Empty;

    public bool HasError => !string.IsNullOrEmpty(target.Error);

    public bool HasLink => target.RemoteUrl is not null;

    public bool CanRetry => canWrite && target.Retryable;

    public string AutomationName => $"{Name}: {StatusText}";

    [RelayCommand]
    private Task OpenAsync() => target.RemoteUrl is { } url ? owner.OpenAsync(url) : Task.CompletedTask;

    [RelayCommand]
    private Task RetryAsync() => owner.RetryTargetAsync(this);

    private static string Label(ILocalizer localizer, string prefix, string value)
    {
        var text = localizer.GetString(prefix + value);
        return text.StartsWith(prefix, StringComparison.Ordinal) ? value : text;
    }
}
