using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Notifications;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Benachrichtigungen je Ereignis: Glocke (nur wo abschaltbar), E-Mail, Discord – nur nutzbare Kanäle. Browser-Push
/// ist für Windows nicht relevant (WNS folgt mit Lücke W1). Dazu Sammel-Mail und Ruhezeit.
/// </summary>
public sealed partial class NotificationSettingsViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly NotificationSettingsService _settings = new(services.Api.Http);
    private bool _ready;

    public ObservableCollection<NotificationTypeRowViewModel> Items { get; } = [];

    public IReadOnlyList<SettingsOption> DigestOptions { get; } = new[] { "instant", "daily", "weekly" }
        .Select(d => new SettingsOption(d, services.Localizer.GetString("NotifySettings_Digest_" + d))).ToList();

    [ObservableProperty]
    public partial SettingsOption? SelectedDigest { get; set; }

    [ObservableProperty]
    public partial string QuietFrom { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string QuietUntil { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    protected override bool HasData => Items.Count > 0;

    public bool IsSignedIn => Services.CurrentUser is not null;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        _ready = false;
        var types = await _settings.TypesAsync(cancellationToken);
        var settings = await _settings.SettingsAsync(cancellationToken);
        Items.Clear();
        foreach (var type in types.OrderBy(t => t.Group, StringComparer.Ordinal).ThenBy(t => t.Label, StringComparer.CurrentCulture))
        {
            Items.Add(new NotificationTypeRowViewModel(type, settings, this));
        }

        SelectedDigest = DigestOptions.FirstOrDefault(d => d.Value == settings.Digest) ?? DigestOptions[0];
        QuietFrom = settings.QuietFrom ?? string.Empty;
        QuietUntil = settings.QuietUntil ?? string.Empty;
        _ready = true;
    }

    partial void OnSelectedDigestChanged(SettingsOption? value)
    {
        if (_ready && value is not null)
        {
            _ = RunAsync(() => _settings.SetDigestAsync(value.Value));
        }
    }

    [RelayCommand]
    private Task SaveQuietHoursAsync()
    {
        static string? Time(string text) => TimeOnly.TryParse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var t) ? t.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : null;
        var from = Time(QuietFrom);
        var until = Time(QuietUntil);
        if ((from is null) != (until is null) || (from is null && (QuietFrom.Length > 0 || QuietUntil.Length > 0)))
        {
            Show(Services.Localizer.GetString(StringKeys.NotifySettingsQuietInvalid), InfoBarSeverity.Warning);
            return Task.CompletedTask;
        }

        return RunAsync(() => _settings.SetQuietHoursAsync(from, until));
    }

    internal Task SetAsync(string key, string channel, bool on) => _ready ? RunAsync(() => _settings.SetChannelAsync(key, channel, on)) : Task.CompletedTask;

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            Show(Services.Localizer.GetString(StringKeys.NotifySettingsSaved), InfoBarSeverity.Success);
        }
        catch (ApiProblemException ex)
        {
            Show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (HttpRequestException)
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

/// <summary>Zeile je Ereignis; <c>bool?</c> wegen <c>CheckBox.IsChecked</c> (TwoWay).</summary>
public sealed partial class NotificationTypeRowViewModel : ObservableObject
{
    private readonly NotificationType _type;
    private readonly NotificationSettingsViewModel _owner;

    public NotificationTypeRowViewModel(NotificationType type, NotificationSettings settings, NotificationSettingsViewModel owner)
    {
        _type = type;
        _owner = owner;
        var effective = settings.Effective(type);
        ShowBell = type.InboxOptional && type.Channels.Contains("in_app");
        ShowMail = type.Channels.Contains("mail") && settings.Available.Contains("mail");
        ShowDiscord = type.Channels.Contains("discord") && settings.Available.Contains("discord");
        Bell = effective.Contains("in_app");
        Mail = effective.Contains("mail");
        Discord = effective.Contains("discord");
    }

    public string Label => _type.Label;

    public bool ShowBell { get; }

    public bool ShowMail { get; }

    public bool ShowDiscord { get; }

    [ObservableProperty]
    public partial bool? Bell { get; set; }

    [ObservableProperty]
    public partial bool? Mail { get; set; }

    [ObservableProperty]
    public partial bool? Discord { get; set; }

    partial void OnBellChanged(bool? value) => _ = _owner.SetAsync(_type.Key, "in_app", value == true);

    partial void OnMailChanged(bool? value) => _ = _owner.SetAsync(_type.Key, "mail", value == true);

    partial void OnDiscordChanged(bool? value) => _ = _owner.SetAsync(_type.Key, "discord", value == true);
}
