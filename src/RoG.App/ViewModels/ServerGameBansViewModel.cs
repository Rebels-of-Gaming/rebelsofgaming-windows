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
using RoG.Core.Features.Servers;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Reiter „Bans &amp; Plugin“ (Website v0.33.0, Guide 43): zentrale Bans, die auf diesem Server gelten (Filter, anlegen,
/// Dauer ändern, aufheben) und der Plugin-Schlüssel des Servers (Status/Heartbeat, Rechte, erstellen/rotieren,
/// widerrufen, Vorfall). Sichtbarkeit nur aus Rechten + Scopes; was der Server ablehnt, meldet er selbst.
/// </summary>
public sealed partial class ServerGameBansViewModel : ServerTabViewModel
{
    private readonly GameBanService _bans;
    private readonly string _serverId;
    private string? _cursor;
    private bool _loaded;

    public ServerGameBansViewModel(AppServices services, string slug, string serverId, ServerAbilities abilities, Action<string, InfoBarSeverity> show)
        : base(services, slug, show)
    {
        _bans = new GameBanService(services.Api.Http);
        _serverId = serverId;
        var me = services.CurrentUser;
        IsManager = me is not null && me.Can(GameBanService.ManagePermission) && me.HasScope(GameBanService.BanScope);
        CanBans = me is not null && me.HasScope(GameBanService.BanScope) && (abilities.Bans || IsManager);
        MayTryKey = me is not null && me.HasScope(GameBanService.KeyScope);
        StatusOptions = GameBanService.Statuses.Select(s => new SettingsOption(s, L("GameBans_Status_" + s))).ToList();
        ScopeOptions = GameBanService.Scopes.Where(s => s != GameBanService.ScopeGlobal || IsManager).Select(s => new SettingsOption(s, L("GameBans_Scope_" + s))).ToList();
        MaxScopeOptions = GameBanService.Scopes.Where(s => s == GameBanService.ScopeServer || IsManager).Select(s => new SettingsOption(s, L("GameBans_Scope_" + s))).ToList();
        SelectedStatus = StatusOptions[0];
        SelectedScope = ScopeOptions.FirstOrDefault(o => o.Value == (IsManager ? GameBanService.ScopeGame : GameBanService.ScopeServer)) ?? ScopeOptions[0];
        SelectedMaxScope = MaxScopeOptions[0];
    }

    /// <summary>Reiter zeigen: Bans (Scope + Recht) oder Plugin-Schlüssel versuchen (Scope; Recht prüft der Server).</summary>
    public bool IsAvailable => CanBans || MayTryKey;

    /// <summary><c>gamebans.manage</c>: alle Geltungen, IP, Plugin-Rechte über „nur anlegen, nur dieser Server“, Vorfall.</summary>
    public bool IsManager { get; }

    public bool CanBans { get; }

    private bool MayTryKey { get; }

    /// <summary>Rückfrage mit optionalem Grund (Titel, Text) → Grund bzw. <c>null</c> = abgebrochen – setzt das Fenster.</summary>
    public Func<string, string, Task<string?>> AskReason { get; set; } = (_, _) => Task.FromResult<string?>(null);

    /// <summary>Neue Dauer und Grund für einen Ban → <c>null</c> = abgebrochen – setzt das Fenster.</summary>
    public Func<GameBanItemViewModel, Task<(int Minutes, string? Reason)?>> AskDuration { get; set; } = _ => Task.FromResult<(int, string?)?>(null);

    /// <summary>Neuen Schlüssel einmalig zeigen (kopierbar) – setzt das Fenster; der Wert wird nirgends gespeichert.</summary>
    public Func<string, Task> ShowToken { get; set; } = _ => Task.CompletedTask;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await Task.WhenAll(CanBans ? LoadBansAsync(reset: true) : Task.CompletedTask, MayTryKey ? LoadKeyAsync() : Task.CompletedTask);
    }

    [RelayCommand]
    private Task ReloadAsync() => Task.WhenAll(CanBans ? LoadBansAsync(reset: true) : Task.CompletedTask, MayTryKey ? LoadKeyAsync() : Task.CompletedTask);

    // --- Bans ------------------------------------------------------------------------------------------------------

    public ObservableCollection<GameBanItemViewModel> Items { get; } = [];

    public IReadOnlyList<SettingsOption> StatusOptions { get; }

    public IReadOnlyList<SettingsOption> ScopeOptions { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedStatus { get; set; }

    partial void OnSelectedStatusChanged(SettingsOption value)
    {
        if (_loaded)
        {
            _ = LoadBansAsync(reset: true);
        }
    }

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [RelayCommand]
    private Task SearchAsync() => LoadBansAsync(reset: true);

    [RelayCommand]
    private Task LoadMoreAsync() => LoadBansAsync(reset: false);

    private async Task LoadBansAsync(bool reset)
    {
        if (!CanBans || IsBusy || (!reset && _cursor is null))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await RunAsync(async () =>
            {
                var page = await _bans.ListAsync(_serverId, SelectedStatus.Value, Search, reset ? null : _cursor);
                if (reset)
                {
                    Items.Clear();
                }

                foreach (var ban in page.Items)
                {
                    Items.Add(new GameBanItemViewModel(ban, this, Services.Localizer));
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

    // Neuer Ban

    [ObservableProperty]
    public partial string Identifier { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string IdentifierType { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PlayerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double Minutes { get; set; } = 1440;

    [ObservableProperty]
    public partial string Reason { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Ip { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SettingsOption SelectedScope { get; set; }

    [RelayCommand]
    private async Task CreateAsync()
    {
        var minutes = double.IsNaN(Minutes) ? 0 : (int)Math.Clamp(Minutes, 0, int.MaxValue);
        var draft = new GameBanDraft(SelectedScope.Value, _serverId, IdentifierType, Identifier, PlayerName, IsManager ? Ip : null, minutes, Reason);
        if (GameBanService.Validate(draft, IsManager) is { Count: > 0 } errors)
        {
            Show(string.Join(" ", errors.Select(L)), InfoBarSeverity.Warning);
            return;
        }

        GameBan? created = null;
        if (await RunAsync(async () => created = await _bans.CreateAsync(draft)))
        {
            Identifier = IdentifierType = PlayerName = Reason = Ip = string.Empty;
            Show(string.Format(CultureInfo.CurrentCulture, L(StringKeys.GameBansCreated), created!.Title), InfoBarSeverity.Success);
            await LoadBansAsync(reset: true);
        }
    }

    internal async Task EditAsync(GameBanItemViewModel item)
    {
        if (await AskDuration(item) is not { } input)
        {
            return;
        }

        GameBan? updated = null;
        if (await RunAsync(async () => updated = await _bans.UpdateAsync(item.Id, input.Minutes, input.Reason)))
        {
            Replace(item, updated!);
            Show(L(StringKeys.GameBansUpdated), InfoBarSeverity.Success);
        }
    }

    internal async Task LiftAsync(GameBanItemViewModel item)
    {
        if (await AskReason(L(StringKeys.GameBansLiftTitle), item.Title) is not { } reason)
        {
            return;
        }

        GameBan? lifted = null;
        if (await RunAsync(async () => lifted = await _bans.LiftAsync(item.Id, reason)))
        {
            if (SelectedStatus.Value == GameBanService.Active)
            {
                Items.Remove(item);
                IsEmpty = Items.Count == 0;
            }
            else
            {
                Replace(item, lifted!);
            }

            Show(L(StringKeys.GameBansLifted), InfoBarSeverity.Success);
        }
    }

    /// <summary>In der öffentlichen Ban-Liste zeigen bzw. ausblenden (ändert für Plugins nichts).</summary>
    internal async Task TogglePublicAsync(GameBanItemViewModel item)
    {
        GameBan? updated = null;
        var show = !item.Ban.IsPublic;
        if (await RunAsync(async () => updated = await _bans.SetPublicAsync(item.Id, show)))
        {
            Replace(item, updated!);
            Show(L(show ? StringKeys.GameBansShown : StringKeys.GameBansHidden), InfoBarSeverity.Success);
        }
    }

    internal void OpenPublic(Uri url) => _ = Services.Browser.OpenAsync(url);

    internal async Task LoadDetailsAsync(GameBanItemViewModel item)
    {
        GameBan? full = null;
        if (await RunAsync(async () => full = await _bans.GetAsync(item.Id)))
        {
            item.SetDeliveries(full!.Deliveries);
        }
    }

    private void Replace(GameBanItemViewModel item, GameBan ban)
    {
        var index = Items.IndexOf(item);
        if (index >= 0)
        {
            Items[index] = new GameBanItemViewModel(ban, this, Services.Localizer);
        }
    }

    // --- Plugin-Schlüssel ------------------------------------------------------------------------------------------

    public IReadOnlyList<SettingsOption> MaxScopeOptions { get; }

    /// <summary>Schlüssel-Bereich: nur, wenn der Server den Status liefert (403 = kein <c>servers.manage</c>).</summary>
    [ObservableProperty]
    public partial bool HasKeySection { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyActionText))]
    public partial bool KeyExists { get; set; }

    [ObservableProperty]
    public partial string KeyStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string KeyDetails { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Brush? KeyBrush { get; set; }

    [ObservableProperty]
    public partial bool MayIssue { get; set; } = true;

    [ObservableProperty]
    public partial bool MayLift { get; set; }

    [ObservableProperty]
    public partial bool MayLiftAny { get; set; }

    [ObservableProperty]
    public partial bool MayIp { get; set; }

    [ObservableProperty]
    public partial bool MayBanMembers { get; set; }

    /// <summary>Minecraft-Konten verknüpfen – nur am Schlüssel des Proxys (Codes für beliebige UUIDs).</summary>
    [ObservableProperty]
    public partial bool MayLink { get; set; }

    [ObservableProperty]
    public partial SettingsOption SelectedMaxScope { get; set; }

    public string KeyActionText => L(KeyExists ? StringKeys.GameBansKeyRotate : StringKeys.GameBansKeyCreate);

    [ObservableProperty]
    public partial string IncidentReason { get; set; } = string.Empty;

    private PluginKeyRights Rights => new(MayIssue, MayLift, MayLiftAny, MayIp, MayBanMembers, SelectedMaxScope.Value, MayLink);

    private async Task LoadKeyAsync()
    {
        try
        {
            ApplyKey(await _bans.KeyAsync(Slug));
            HasKeySection = true;
        }
        catch (ApiProblemException ex) when (ex.Problem.Status is 403 or 404)
        {
            HasKeySection = false;
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    private void ApplyKey(PluginKeyStatus key)
    {
        var l = Services.Localizer;
        KeyExists = key.Exists && key.Active;
        var rights = key.Exists ? PluginKeyRights.From(key) : PluginKeyRights.Minimal;
        (MayIssue, MayLift, MayLiftAny, MayIp, MayBanMembers, MayLink) = (rights.MayIssue, rights.MayLift, rights.MayLiftAny, rights.MayIp, rights.MayBanMembers, rights.MayLink);
        SelectedMaxScope = MaxScopeOptions.FirstOrDefault(o => o.Value == rights.MaxScope) ?? MaxScopeOptions[0];
        KeyStatus = !KeyExists
            ? l.GetString(key.RevokedAt is null ? StringKeys.GameBansKeyNone : StringKeys.GameBansKeyRevoked)
            : l.GetString(key.PluginActive ? StringKeys.GameBansPluginActive : StringKeys.GameBansPluginInactive);
        KeyBrush = (Brush)Application.Current.Resources[!KeyExists ? "RoGMutedBrush" : key.PluginActive ? "RoGGreenBrush" : "RoGGoldBrush"];
        KeyDetails = KeyExists ? string.Join(" · ", new[]
        {
            key.Hint,
            key.Plugin is { } p ? p + (key.PluginVersion is { } v ? " " + v : string.Empty) : null,
            key.PluginOutdated ? l.GetString(StringKeys.GameBansPluginOutdated) : null,
            key.GameVersion,
            key.PlayersOnline is { } n ? string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.GameBansPlayersOnline), n) : null,
            key.LastHeartbeatAt is { } hb ? string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.GameBansLastHeartbeat), hb.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)) : null,
        }.Where(s => !string.IsNullOrEmpty(s))) : string.Empty;
    }

    [RelayCommand]
    private async Task CreateKeyAsync()
    {
        if (KeyExists && !await Confirm(L(StringKeys.GameBansKeyRotate), L(StringKeys.GameBansKeyRotateText)))
        {
            return;
        }

        var rights = IsManager ? Rights : PluginKeyRights.Minimal with { MayIssue = MayIssue };
        PluginKeyCreated? created = null;
        if (await RunAsync(async () => created = await _bans.CreateKeyAsync(Slug, rights)))
        {
            ApplyKey(created!.Status);
            await ShowToken(created.Token);
        }
    }

    [RelayCommand]
    private async Task SaveRightsAsync()
    {
        var rights = Rights;
        if (rights.NeedsManage && !IsManager)
        {
            Show(L(StringKeys.GameBansRightsNeedManage), InfoBarSeverity.Warning);
            return;
        }

        PluginKeyStatus? key = null;
        if (await RunAsync(async () => key = await _bans.UpdateKeyAsync(Slug, rights)))
        {
            ApplyKey(key!);
            Show(L(StringKeys.GameBansRightsSaved), InfoBarSeverity.Success);
        }
    }

    [RelayCommand]
    private async Task RevokeKeyAsync()
    {
        if (await Confirm(L(StringKeys.GameBansKeyRevoke), L(StringKeys.GameBansKeyRevokeText))
            && await RunAsync(() => _bans.RevokeKeyAsync(Slug)))
        {
            Show(L(StringKeys.GameBansKeyRevokedDone), InfoBarSeverity.Success);
            await LoadKeyAsync();
        }
    }

    /// <summary>Vorfall: alle Bans aus dem Plugin dieses Servers aufheben (nur <c>gamebans.manage</c>).</summary>
    [RelayCommand]
    private async Task LiftPluginBansAsync()
    {
        if (!IsManager || !await Confirm(L(StringKeys.GameBansIncidentTitle), L(StringKeys.GameBansIncidentText)))
        {
            return;
        }

        var count = 0;
        if (await RunAsync(async () => count = await _bans.LiftPluginBansAsync(Slug, IncidentReason, null)))
        {
            IncidentReason = string.Empty;
            Show(string.Format(CultureInfo.CurrentCulture, L(StringKeys.GameBansIncidentDone), count), InfoBarSeverity.Success);
            await LoadBansAsync(reset: true);
        }
    }
}

public sealed partial class GameBanItemViewModel(GameBan ban, ServerGameBansViewModel owner, ILocalizer localizer) : ObservableObject
{
    public string Id => ban.Id;

    public string Title => ban.Title;

    public GameBan Ban => ban;

    public string Identity => $"{ban.IdentifierType}: {ban.Identifier}" + (ban.Ip is { } ip ? " · " + ip : string.Empty);

    public bool IsActive => ban.IsActive;

    public string StatusText => localizer.GetString("GameBans_Status_" + ban.Status);

    public Brush StatusBrush => (Brush)Application.Current.Resources[ban.Status switch
    {
        GameBanService.Active => "RoGRedBrush",
        GameBanService.Lifted => "RoGGreenBrush",
        _ => "RoGMutedBrush",
    }];

    public string Meta { get; } = string.Join(" · ", new[]
    {
        ban.Scope switch
        {
            GameBanService.ScopeGlobal => localizer.GetString("GameBans_Scope_global"),
            GameBanService.ScopeServer => ban.ServerName ?? localizer.GetString("GameBans_Scope_server"),
            _ => ban.GameName ?? localizer.GetString("GameBans_Scope_game"),
        },
        ban.Reason,
        ban.LiftedAt is { } lifted
            ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.GameBansLiftedAt), lifted.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), ban.LiftedBy ?? "—")
            : ban.ExpiresAt is { } e
                ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.BanUntil), e.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                : localizer.GetString(StringKeys.BanPermanent),
        ban.IssuedBy is { } issued
            ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.GameBansIssuedIngame), issued, ban.SourceServer ?? "—")
            : ban.CreatedBy is { } by ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.GameBansCreatedBy), by) : null,
        ban.UserName is { } user ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.GameBansMember), user) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>Öffentliche Ban-Liste: „ausgeblendet“ bzw. Einspruchs-Code (nur für zuständige Admins).</summary>
    public string PublicInfo { get; } = string.Join(" · ", new[]
    {
        ban.IsPublic ? null : localizer.GetString(StringKeys.GameBansHiddenBadge),
        ban.AppealCode is { } code ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.GameBansAppealCode), code) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool HasPublicInfo => PublicInfo.Length > 0;

    public string ToggleText => localizer.GetString(ban.IsPublic ? StringKeys.GameBansHide : StringKeys.GameBansShow);

    public bool HasPublicUrl => ban.PublicUrl is not null && ban.IsPublic;

    /// <summary>RCON-Zustellungen (aus dem Einzelabruf).</summary>
    [ObservableProperty]
    public partial string Deliveries { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasDeliveries { get; set; }

    internal void SetDeliveries(IReadOnlyList<GameBanDelivery> deliveries)
    {
        Deliveries = deliveries.Count == 0
            ? localizer.GetString(StringKeys.GameBansNoDeliveries)
            : string.Join(Environment.NewLine, deliveries.Select(d => string.Join(" · ", new[]
            {
                d.ServerName ?? "—",
                localizer.GetString("GameBans_Delivery_" + d.Status),
                d.Error,
            }.Where(s => !string.IsNullOrEmpty(s)))));
        HasDeliveries = true;
    }

    [RelayCommand]
    private Task EditAsync() => owner.EditAsync(this);

    [RelayCommand]
    private Task LiftAsync() => owner.LiftAsync(this);

    [RelayCommand]
    private Task DetailsAsync() => owner.LoadDetailsAsync(this);

    [RelayCommand]
    private Task TogglePublicAsync() => owner.TogglePublicAsync(this);

    [RelayCommand]
    private void OpenPublic()
    {
        if (ban.PublicUrl is { } url)
        {
            owner.OpenPublic(url);
        }
    }
}
