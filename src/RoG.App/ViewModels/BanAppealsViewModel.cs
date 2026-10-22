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
/// Verwaltung → Einsprüche (Website v0.34.0, Guide 43): Einsprüche gegen Gameserver-Bans annehmen (hebt den Ban auf)
/// oder ablehnen (optional sofort neuer Einspruch erlaubt); Forum-Bereich für Einsprüche mit <c>gamebans.manage</c>.
/// Sichtbar mit Scope <c>gamebans:manage</c> und <c>gamebans.manage</c> bzw. <c>servers.bans</c> – was jemand
/// entscheiden darf, sagt der Server je Einspruch (<c>can_decide</c>).
/// </summary>
public sealed partial class BanAppealsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<string, InfoBarSeverity> _show;
    private readonly PublicBanService _appeals;
    private string? _cursor;
    private bool _loaded;

    public BanAppealsViewModel(AppServices services, Action<string, InfoBarSeverity> show)
    {
        _services = services;
        _show = show;
        _appeals = new PublicBanService(services.Api.Http);
        var me = services.CurrentUser;
        var scope = me is not null && me.HasScope(GameBanService.BanScope);
        CanView = scope && (me!.CanAnywhere(GameBanService.ManagePermission) || me.CanAnywhere("servers.bans"));
        IsManager = scope && me!.Can(GameBanService.ManagePermission);
        StatusOptions = PublicBanService.AppealStatuses.Select(s => new SettingsOption(s, L("BanAppeals_Status_" + s))).ToList();
        SelectedStatus = StatusOptions[0];
    }

    public bool CanView { get; }

    /// <summary>Forum-Bereich für Einsprüche wählen/anlegen.</summary>
    public bool IsManager { get; }

    public ObservableCollection<BanAppealItemViewModel> Items { get; } = [];

    public IReadOnlyList<SettingsOption> StatusOptions { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedStatus { get; set; }

    partial void OnSelectedStatusChanged(SettingsOption value)
    {
        if (_loaded)
        {
            _ = LoadAsync(reset: true);
        }
    }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    /// <summary>Entscheidung abfragen (Titel) → Begründung + „neuer Einspruch sofort erlaubt“, <c>null</c> = abgebrochen – setzt die Seite.</summary>
    public Func<string, bool, Task<(string? Note, bool Reappeal)?>> AskDecision { get; set; } = (_, _) => Task.FromResult<(string?, bool)?>(null);

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await Task.WhenAll(LoadAsync(reset: true), IsManager ? LoadSettingsAsync() : Task.CompletedTask);
    }

    [RelayCommand]
    private Task ReloadAsync() => Task.WhenAll(LoadAsync(reset: true), IsManager ? LoadSettingsAsync() : Task.CompletedTask);

    [RelayCommand]
    private Task MoreAsync() => LoadAsync(reset: false);

    private async Task LoadAsync(bool reset)
    {
        if (!CanView || IsBusy || (!reset && _cursor is null))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await RunAsync(async () =>
            {
                var page = await _appeals.AppealsAsync(SelectedStatus.Value, reset ? null : _cursor);
                if (reset)
                {
                    Items.Clear();
                }

                foreach (var appeal in page.Items)
                {
                    Items.Add(new BanAppealItemViewModel(appeal, this, _services.Localizer));
                }

                _cursor = page.NextCursor;
                HasMore = page.HasMore;
                IsEmpty = Items.Count == 0;
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task DecideAsync(BanAppealItemViewModel item, bool accept)
    {
        var title = string.Format(CultureInfo.CurrentCulture, L(accept ? StringKeys.BanAppealsAcceptTitle : StringKeys.BanAppealsRejectTitle), item.Title);
        if (await AskDecision(title, accept) is not { } input)
        {
            return;
        }

        BanAppeal? decided = null;
        if (await RunAsync(async () => decided = await _appeals.DecideAsync(item.Id, accept, input.Note, input.Reappeal)))
        {
            var index = Items.IndexOf(item);
            if (index >= 0)
            {
                if (SelectedStatus.Value == PublicBanService.AppealOpen)
                {
                    Items.RemoveAt(index);
                }
                else
                {
                    Items[index] = new BanAppealItemViewModel(decided!, this, _services.Localizer);
                }
            }

            IsEmpty = Items.Count == 0;
            _show(L(accept ? StringKeys.BanAppealsAccepted : StringKeys.BanAppealsRejected), InfoBarSeverity.Success);
        }
    }

    internal void Open(Uri url)
    {
        if (AppRoute.FromWebUrl(url, _services.Environment) is not { } route || !_services.Navigate(route))
        {
            _ = _services.Browser.OpenAsync(url);
        }
    }

    // --- Forum-Bereich ---------------------------------------------------------------------------------------------

    public ObservableCollection<SettingsOption> BoardOptions { get; } = [];

    [ObservableProperty]
    public partial SettingsOption? SelectedBoard { get; set; }

    [ObservableProperty]
    public partial string BoardText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSettings { get; set; }

    /// <summary>Admin-Einträge der Mindest-Plugin-Versionen, eine Zeile je Plugin („rog-proxy 0.6.0“).</summary>
    [ObservableProperty]
    public partial string PluginVersions { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PluginVersionsInfo { get; set; } = string.Empty;

    [RelayCommand]
    private async Task SavePluginVersionsAsync()
    {
        if (!IsManager)
        {
            return;
        }

        if (PublicBanService.ParsePluginVersions(PluginVersions) is not { } versions)
        {
            _show(L(StringKeys.BanAppealsPluginVersionsFormat), InfoBarSeverity.Warning);
            return;
        }

        AppealSettings? settings = null;
        if (await RunAsync(async () => settings = await _appeals.SetPluginVersionsAsync(versions)))
        {
            ApplySettings(settings!);
            _show(L(StringKeys.BanAppealsPluginVersionsSaved), InfoBarSeverity.Success);
        }
    }

    private async Task LoadSettingsAsync()
    {
        AppealSettings? settings = null;
        if (await RunAsync(async () => settings = await _appeals.SettingsAsync()))
        {
            ApplySettings(settings!);
        }
    }

    private void ApplySettings(AppealSettings settings)
    {
        BoardOptions.Clear();
        BoardOptions.Add(new SettingsOption(string.Empty, L(StringKeys.BanAppealsNoBoard)));
        foreach (var (id, name) in settings.BoardOptions)
        {
            BoardOptions.Add(new SettingsOption(id, name));
        }

        SelectedBoard = BoardOptions.FirstOrDefault(o => o.Value == (settings.BoardId ?? string.Empty)) ?? BoardOptions[0];
        BoardText = settings.BoardId is null
            ? L(StringKeys.BanAppealsBoardMissing)
            : string.Format(CultureInfo.CurrentCulture, L(settings.BoardPublic ? StringKeys.BanAppealsBoardActive : StringKeys.BanAppealsBoardNotPublic), settings.BoardName ?? settings.BoardId);
        PluginVersions = PublicBanService.FormatPluginVersions(settings.PluginVersions);
        var env = settings.EnvPluginVersions.Count > 0 ? string.Join(", ", settings.EnvPluginVersions.Select(v => v.Plugin + " " + v.Version)) : "—";
        PluginVersionsInfo = string.Format(CultureInfo.CurrentCulture, L(StringKeys.BanAppealsPluginVersionsInfo), settings.MinPluginVersion ?? "1.0.0", env);
        HasSettings = true;
    }

    [RelayCommand]
    private async Task SaveBoardAsync()
    {
        AppealSettings? settings = null;
        if (IsManager && await RunAsync(async () => settings = await _appeals.SetBoardAsync(SelectedBoard?.Value)))
        {
            ApplySettings(settings!);
            _show(L(StringKeys.BanAppealsBoardSaved), InfoBarSeverity.Success);
        }
    }

    [RelayCommand]
    private async Task CreateBoardAsync()
    {
        AppealSettings? settings = null;
        if (IsManager && await RunAsync(async () => settings = await _appeals.CreateBoardAsync()))
        {
            ApplySettings(settings!);
            _show(L(StringKeys.BanAppealsBoardSaved), InfoBarSeverity.Success);
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
            _show(_services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (System.Text.Json.JsonException)
        {
            _show(_services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }

        return false;
    }

    private string L(string key) => _services.Localizer.GetString(key);
}

public sealed partial class BanAppealItemViewModel(BanAppeal appeal, BanAppealsViewModel owner, ILocalizer localizer)
{
    public string Id => appeal.Id;

    public string Title => appeal.BanName ?? appeal.UserName ?? appeal.Id;

    public string StatusText => localizer.GetString("BanAppeals_Status_" + appeal.Status);

    public Brush StatusBrush => (Brush)Application.Current.Resources[appeal.Status switch
    {
        PublicBanService.AppealOpen => "RoGGoldBrush",
        PublicBanService.AppealAccepted => "RoGGreenBrush",
        _ => "RoGMutedBrush",
    }];

    public string Meta { get; } = string.Join(" · ", new[]
    {
        appeal.UserName is { } user ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.BanAppealsBy), user) : null,
        appeal.Proof is { } proof ? localizer.GetString("BanAppeals_Proof_" + proof) : null,
        appeal.CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        appeal.BanStatus is { } status ? localizer.GetString("GameBans_Status_" + status) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string Note => appeal.DecisionNote ?? string.Empty;

    public bool HasNote => !string.IsNullOrEmpty(appeal.DecisionNote);

    /// <summary>Offen und laut Server entscheidbar (Geltung, Rang).</summary>
    public bool CanDecide => appeal.IsOpen && appeal.CanDecide;

    public bool HasThread => appeal.ThreadUrl is not null;

    public bool HasBan => appeal.BanUrl is not null;

    [RelayCommand]
    private Task AcceptAsync() => owner.DecideAsync(this, accept: true);

    [RelayCommand]
    private Task RejectAsync() => owner.DecideAsync(this, accept: false);

    [RelayCommand]
    private void OpenThread()
    {
        if (appeal.ThreadUrl is { } url)
        {
            owner.Open(url);
        }
    }

    [RelayCommand]
    private void OpenBan()
    {
        if (appeal.BanUrl is { } url)
        {
            owner.Open(url);
        }
    }
}
