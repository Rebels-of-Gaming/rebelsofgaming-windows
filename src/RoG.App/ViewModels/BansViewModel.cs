using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.GameBans;
using RoG.Core.Localization;
using RoG.Core.Navigation;

namespace RoG.App.ViewModels;

/// <summary>
/// Öffentliche Ban-Liste (Website v0.34.0, Guide 43, Annahme 488): aktive bzw. kürzlich beendete Bans mit gekürzter
/// Kennung, Detail mit Einspruchs-Status und Einspruch (Scope <c>gamebans:appeal</c>; Nachweis prüft der Server).
/// </summary>
public sealed partial class BansViewModel : LoadableViewModel
{
    private readonly PublicBanService _bans;
    private string? _cursor;

    public BansViewModel(AppServices services)
        : base(services)
    {
        _bans = new PublicBanService(services.Api.Http);
        StatusOptions = PublicBanService.Statuses.Select(s => new SettingsOption(s, services.Localizer.GetString("Bans_Status_" + s))).ToList();
        SelectedStatus = StatusOptions[0];
        Detail = new BanDetailViewModel(services, _bans);
    }

    public ObservableCollection<PublicBanItemViewModel> Items { get; } = [];

    public IReadOnlyList<SettingsOption> StatusOptions { get; }

    public BanDetailViewModel Detail { get; }

    protected override bool HasData => Items.Count > 0;

    [ObservableProperty]
    public partial SettingsOption SelectedStatus { get; set; }

    partial void OnSelectedStatusChanged(SettingsOption value) => _ = LoadAsync();

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial PublicBanItemViewModel? Selected { get; set; }

    /// <summary>Detail per Link geöffnet (ohne Auswahl in der Liste).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial bool DetailForced { get; set; }

    public bool IsDetailOpen => Selected is not null || DetailForced;

    public bool ShowSelectHint => !IsDetailOpen;

    partial void OnSelectedChanged(PublicBanItemViewModel? value)
    {
        if (value is not null)
        {
            _ = Detail.OpenAsync(value.Id);
        }
    }

    /// <summary>Link auf einen Ban (<c>/bans/{id}</c>, <c>rog://bans/{id}</c>): Liste laden und Detail zeigen.</summary>
    public async Task OpenAsync(string id)
    {
        DetailForced = true;
        await Task.WhenAll(LoadAsync(), Detail.OpenAsync(id));
    }

    [RelayCommand]
    private Task SearchNowAsync() => LoadAsync();

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var page = await _bans.ListAsync(SelectedStatus.Value, Search, null, cancellationToken);
        Items.Clear();
        foreach (var ban in page.Items)
        {
            Items.Add(new PublicBanItemViewModel(ban, Services.Localizer));
        }

        _cursor = page.NextCursor;
        HasMore = page.HasMore;
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_cursor is null)
        {
            return;
        }

        try
        {
            var page = await _bans.ListAsync(SelectedStatus.Value, Search, _cursor);
            foreach (var ban in page.Items)
            {
                Items.Add(new PublicBanItemViewModel(ban, Services.Localizer));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        }
        catch (ApiProblemException ex)
        {
            ErrorMessage = Services.Localizer.GetError(ex.Problem.Code);
        }
        catch (System.Text.Json.JsonException)
        {
            ErrorMessage = Services.Localizer.GetError(ApiProblem.Codes.Unknown);
        }
    }

    internal static Brush StatusBrush(string status) =>
        (Brush)Application.Current.Resources[status == PublicBanService.Active ? "RoGRedBrush" : "RoGMutedBrush"];

    /// <summary>„bis …“, „dauerhaft“ oder „beendet am …“.</summary>
    internal static string Period(PublicBan ban, ILocalizer l) =>
        ban.EndedAt is { } ended
            ? string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.BansEndedAt), ended.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : ban.ExpiresAt is { } until && !ban.Permanent
                ? string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.BanUntil), until.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                : l.GetString(StringKeys.BanPermanent);
}

public sealed class PublicBanItemViewModel(PublicBan ban, ILocalizer localizer)
{
    public string Id => ban.Id;

    public string Title => ban.Title;

    public Brush StatusBrush => BansViewModel.StatusBrush(ban.Status);

    public string Identity => $"{ban.IdentifierType} · {ban.IdentifierMasked}";

    public string Meta { get; } = string.Join(" · ", new[]
    {
        ban.ServerName ?? ban.GameName ?? ban.ScopeLabel,
        ban.Reason,
        BansViewModel.Period(ban, localizer),
        ban.Appeal is { } a ? localizer.GetString("Bans_Appeal_" + a.Status) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>Ein öffentlicher Ban mit Einspruchs-Status und Formular.</summary>
public sealed partial class BanDetailViewModel(AppServices services, PublicBanService bans) : LoadableViewModel(services)
{
    private string? _id;
    private PublicBan? _ban;

    protected override bool HasData => _ban is not null;

    public async Task OpenAsync(string id)
    {
        _id = id;
        Statement = Code = string.Empty;
        IsFeedbackOpen = false;
        await LoadAsync();
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_id is null)
        {
            return;
        }

        _ban = null;
        Apply(await bans.GetAsync(_id, cancellationToken));
    }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Brush? StatusBrushValue { get; set; }

    [ObservableProperty]
    public partial string Identity { get; set; } = string.Empty;

    /// <summary>Spiel/Server, Geltung, Beginn, Ende.</summary>
    [ObservableProperty]
    public partial string Facts { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Reason { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AppealText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasThread { get; set; }

    /// <summary>Formular zeigen: Einspruch möglich, angemeldet, Scope vorhanden.</summary>
    [ObservableProperty]
    public partial bool CanAppeal { get; set; }

    [ObservableProperty]
    public partial bool NeedsCode { get; set; }

    /// <summary>Warum gerade kein Einspruch geht (oder dass der Code nötig ist).</summary>
    [ObservableProperty]
    public partial string AppealHint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasAppealHint { get; set; }

    [ObservableProperty]
    public partial string Statement { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Code { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSending { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    [ObservableProperty]
    public partial string FeedbackMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    private void Apply(PublicBan ban)
    {
        var l = Services.Localizer;
        _ban = ban;
        Title = ban.Title;
        StatusText = l.GetString("Bans_Status_" + (ban.IsActive ? PublicBanService.Active : PublicBanService.Ended));
        StatusBrushValue = BansViewModel.StatusBrush(ban.Status);
        Identity = $"{ban.IdentifierType} · {ban.IdentifierMasked}";
        Facts = string.Join(Environment.NewLine, new[]
        {
            ban.GameName,
            ban.ServerName,
            ban.ScopeLabel,
            ban.CreatedAt is { } from ? Format(StringKeys.BansSince, from.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)) : null,
            BansViewModel.Period(ban, l),
        }.Where(s => !string.IsNullOrEmpty(s)));
        Reason = ban.Reason ?? string.Empty;

        var info = ban.AppealInfo;
        var status = info?.Status ?? ban.Appeal?.Status;
        AppealText = status is null ? l.GetString(StringKeys.BansAppealNone) : l.GetString("Bans_Appeal_" + status);
        HasThread = info?.ThreadUrl is not null;

        var me = Services.CurrentUser;
        var hasScope = me is not null && me.HasScope(PublicBanService.AppealScope);
        CanAppeal = info is { Available: true } && hasScope;
        NeedsCode = CanAppeal && info!.NeedsCode;
        AppealHint = info switch
        {
            null => string.Empty,
            { Available: false, Blocked: "cooldown", CooldownUntil: { } until } => Format(StringKeys.BansCooldownUntil, until.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)),
            { Available: false, Blocked: { } blocked } => l.GetString("Bans_Blocked_" + blocked),
            { Available: true } when me is null => l.GetString(StringKeys.BansSignInToAppeal),
            { Available: true } when !hasScope => l.GetString(StringKeys.BansScopeMissing),
            { Available: true, NeedsCode: true } => l.GetString(StringKeys.BansCodeHint),
            _ => l.GetString(StringKeys.BansProofHint),
        };
        HasAppealHint = AppealHint.Length > 0;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (_ban is null || IsSending)
        {
            return;
        }

        var l = Services.Localizer;
        if (PublicBanService.ValidateAppeal(Statement, Code, NeedsCode) is { Count: > 0 } errors)
        {
            ShowFeedback(string.Join(" ", errors.Select(l.GetString)), InfoBarSeverity.Warning);
            return;
        }

        IsSending = true;
        try
        {
            var appeal = await bans.AppealAsync(_ban.Id, Statement, Code);
            Statement = Code = string.Empty;
            ShowFeedback(l.GetString(StringKeys.BansAppealSent), InfoBarSeverity.Success);
            await LoadAsync(silent: true);
            if (appeal.ThreadUrl is { } thread)
            {
                OpenUrl(thread);
            }
        }
        catch (ApiProblemException ex)
        {
            ShowFeedback(ex.Problem.Code == "validation_failed" && !string.IsNullOrWhiteSpace(Code)
                ? l.GetString(StringKeys.BansCodeWrong)
                : l.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (System.Text.Json.JsonException)
        {
            ShowFeedback(l.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private void OpenThread()
    {
        if (_ban?.AppealInfo?.ThreadUrl is { } thread)
        {
            OpenUrl(thread);
        }
    }

    [RelayCommand]
    private void OpenWeb()
    {
        if (_ban?.Url is { } url)
        {
            _ = Services.Browser.OpenAsync(url);
        }
    }

    /// <summary>Forum-Thema in der App öffnen, sonst im Browser.</summary>
    private void OpenUrl(Uri url)
    {
        if (AppRoute.FromWebUrl(url, Services.Environment) is not { } route || !Services.Navigate(route))
        {
            _ = Services.Browser.OpenAsync(url);
        }
    }

    private void ShowFeedback(string message, InfoBarSeverity severity)
    {
        FeedbackMessage = message;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }
}
