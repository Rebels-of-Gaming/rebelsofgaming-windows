using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Minecraft;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Eintrag der Weltauswahl („Server · Welt“).</summary>
public sealed record MapWorldOption(MapServer Server, MapWorld World, string Label);

/// <summary>Warp oder Live-Spieler in der Seitenliste; Klick zentriert die Karte.</summary>
public sealed record MapPointItem(string Label, string Detail, double X, double Z);

/// <summary>
/// Minecraft-Weltkarte (Website v0.44.0, Guide 48): Welt wählen, Kacheln über den Kachel-Index je Region, Warps und
/// Live-Spieler (alle 5 s, nur wenn die Website sie diesem Betrachter zeigt). Live-Positionen werden nicht gespeichert.
/// Verwaltende (<c>minecraft:manage</c> + <c>minecraft.manage</c>) stellen Live-Spieler um, blenden Welten/Server aus,
/// benennen Welten je Sprache und löschen Kacheln.
/// </summary>
public sealed partial class MinecraftMapViewModel(AppServices services) : ObservableObject
{
    private readonly MinecraftService _minecraft = new(services.Api.Http);
    private MapLink _link = new(null, null, null, null);
    private bool _liveBusy;
    private bool _applyingMode;

    public ObservableCollection<MapWorldOption> Worlds { get; } = [];

    public ObservableCollection<MapPointItem> Players { get; } = [];

    public ObservableCollection<MapPointItem> Warps { get; } = [];

    public IReadOnlyList<SettingsOption> LiveModeOptions { get; } = MinecraftService.LiveModes
        .Select(m => new SettingsOption(m, services.Localizer.GetString("MinecraftMap_Live_" + m)))
        .ToList();

    /// <summary>Welt gewechselt: Karte neu aufbauen, optional auf eine Stelle aus dem Link zentrieren.</summary>
    public event EventHandler<(MapWorld? World, int? X, int? Z)>? WorldChanged;

    /// <summary>Live-Spieler neu (für die Karte).</summary>
    public event EventHandler<IReadOnlyList<MapLivePlayer>>? PlayersChanged;

    /// <summary>Kachel-Index nach Änderungen (Kacheln gelöscht) neu laden.</summary>
    public event EventHandler? TilesReset;

    public Func<string, string, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorld), nameof(WorldInfo), nameof(HasNoTiles), nameof(CanEditWorld))]
    public partial MapWorldOption? SelectedWorld { get; set; }

    [ObservableProperty]
    public partial MapOverview? Overview { get; set; }

    /// <summary>Live-Spieler anzeigen (Schalter der Seite; nur wirksam, wenn die Website sie zeigt).</summary>
    [ObservableProperty]
    public partial bool ShowLive { get; set; } = true;

    [ObservableProperty]
    public partial string? LiveInfo { get; set; }

    public bool HasWorld => SelectedWorld is not null;

    public bool IsEmpty => !IsLoading && string.IsNullOrEmpty(ErrorMessage) && Worlds.Count == 0;

    public bool LiveAvailable => Overview?.LivePlayers == true;

    public bool HasPlayers => Players.Count > 0;

    public bool HasWarps => Warps.Count > 0;

    /// <summary>Kachelzahl und Stand der gewählten Welt.</summary>
    public string WorldInfo => SelectedWorld?.World is not { } w
        ? string.Empty
        : string.Format(
            CultureInfo.CurrentCulture,
            L(w.TilesUpdatedAt is null ? StringKeys.MinecraftMapWorldInfoNoDate : StringKeys.MinecraftMapWorldInfo),
            w.TileCount,
            L("MinecraftMap_Env_" + (w.Environment is "nether" or "the_end" ? w.Environment : "normal")),
            w.TilesUpdatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? string.Empty);

    public bool HasNoTiles => SelectedWorld?.World.TileCount == 0;

    // --- Verwaltung -------------------------------------------------------------------------------------------------

    public bool CanManage => services.CurrentUser is { } me && me.Can(MinecraftService.ManagePermission) && me.HasScope(MinecraftService.ManageScope);

    public bool CanEditWorld => CanManage && SelectedWorld is not null;

    [ObservableProperty]
    public partial SettingsOption? SelectedLiveMode { get; set; }

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial bool EditWorldHidden { get; set; }

    [ObservableProperty]
    public partial bool EditServerHidden { get; set; }

    [ObservableProperty]
    public partial string EditNameDe { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditNameEn { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditNameFr { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? QuotaText { get; set; }

    [ObservableProperty]
    public partial string? WorldAdminInfo { get; set; }

    public bool HasQuota => !string.IsNullOrEmpty(QuotaText);

    /// <summary>Kachel-Index einer Region für die Karte (Fehler fängt die Karte ab und zeigt die Region leer).</summary>
    public Task<IReadOnlyList<MapTileRef>> LoadTilesAsync(string world, int zoom, int regionX, int regionZ) =>
        _minecraft.MapTilesAsync(world, zoom, regionX, regionZ);

    /// <summary>Link aus dem Spiel/von der Website (<c>server</c>, <c>world</c>, <c>x</c>, <c>z</c>) für den nächsten Ladevorgang.</summary>
    public void UseLink(string? key) => _link = MapLink.Parse(key);

    public async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var previous = SelectedWorld?.World.Id;
            var map = await _minecraft.MapAsync();
            Overview = map;
            ErrorMessage = null;
            Worlds.Clear();
            foreach (var server in map.Servers)
            {
                foreach (var world in server.Worlds)
                {
                    var label = server.Worlds.Count > 1 || map.Servers.Count > 1 ? $"{server.Name} · {world.Name}" : world.Name;
                    if (world.Hidden || server.Hidden)
                    {
                        label += " · " + L(StringKeys.MinecraftMapHidden);
                    }

                    Worlds.Add(new MapWorldOption(server, world, label));
                }
            }

            ApplyAdmin(map);
            var linked = Linked();
            SelectedWorld = linked ?? Worlds.FirstOrDefault(w => w.World.Id == previous) ?? Worlds.FirstOrDefault();
            WorldChanged?.Invoke(this, (SelectedWorld?.World, linked is null ? null : _link.X, linked is null ? null : _link.Z));
            _link = new MapLink(null, null, null, null);
            UpdateLiveInfo();
            await RefreshLiveAsync();
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException)
        {
            ErrorMessage = ErrorText(ex);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
        TilesReset?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Live-Spieler des gewählten Servers (Seite ruft alle 5 s); ohne Freigabe oder Schalter aus: leer.</summary>
    public async Task RefreshLiveAsync()
    {
        if (_liveBusy)
        {
            return;
        }

        if (SelectedWorld is not { } option || !LiveAvailable || !ShowLive)
        {
            SetPlayers([]);
            return;
        }

        _liveBusy = true;
        try
        {
            var live = await _minecraft.MapLiveAsync(option.Server.NetworkId is { } n && MinecraftMapGeometry.IsValidName(n) ? n : option.Server.Id);
            SetPlayers(live.Players);
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException or ArgumentException)
        {
            SetPlayers([]); // Live-Stand ist Beiwerk – Karte bleibt nutzbar
        }
        finally
        {
            _liveBusy = false;
        }
    }

    partial void OnSelectedWorldChanged(MapWorldOption? value)
    {
        Warps.Clear();
        foreach (var marker in value?.World.Markers.OrderBy(m => m.Label, StringComparer.CurrentCultureIgnoreCase) ?? Enumerable.Empty<MapMarker>())
        {
            Warps.Add(new MapPointItem(marker.Label, Coordinates(marker.X, marker.Z), marker.X + 0.5, marker.Z + 0.5));
        }

        OnPropertyChanged(nameof(HasWarps));
        FillWorldAdmin();
        if (!IsLoading)
        {
            WorldChanged?.Invoke(this, (value?.World, null, null));
            _ = RefreshLiveAsync();
        }
    }

    partial void OnShowLiveChanged(bool value) => _ = RefreshLiveAsync();

    async partial void OnSelectedLiveModeChanged(SettingsOption? value)
    {
        if (_applyingMode || value is null || !CanManage || value.Value == Overview?.LiveMode)
        {
            return;
        }

        try
        {
            var mode = await _minecraft.SetMapLiveModeAsync(value.Value);
            ShowFeedback(string.Format(CultureInfo.CurrentCulture, L(StringKeys.MinecraftMapLiveSaved), L("MinecraftMap_Live_" + mode)), InfoBarSeverity.Success);
            await LoadAsync();
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException)
        {
            ShowFeedback(ErrorText(ex), InfoBarSeverity.Error);
            ApplyAdmin(Overview);
        }
    }

    [RelayCommand]
    private async Task SaveWorldAsync()
    {
        if (!CanManage || SelectedWorld is not { } option || IsSaving)
        {
            return;
        }

        IsSaving = true;
        try
        {
            var names = new Dictionary<string, string?> { ["de"] = EditNameDe, ["en"] = EditNameEn, ["fr"] = EditNameFr };
            await _minecraft.UpdateMapWorldAsync(option.World.Id, EditWorldHidden, option.World.Names is null ? null : names);
            if (EditServerHidden != option.Server.Hidden)
            {
                await _minecraft.SetMapServerHiddenAsync(option.Server.Id, EditServerHidden);
            }

            ShowFeedback(L(StringKeys.MinecraftMapSaved), InfoBarSeverity.Success);
            await LoadAsync();
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException or ArgumentException)
        {
            ShowFeedback(ErrorText(ex), InfoBarSeverity.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ClearTilesAsync()
    {
        if (!CanManage || SelectedWorld is not { } option || IsSaving)
        {
            return;
        }

        if (!await Confirm(L(StringKeys.MinecraftMapClearTitle), string.Format(CultureInfo.CurrentCulture, L(StringKeys.MinecraftMapClearText), option.Label)))
        {
            return;
        }

        IsSaving = true;
        try
        {
            await _minecraft.ClearMapTilesAsync(option.World.Id);
            ShowFeedback(L(StringKeys.MinecraftMapCleared), InfoBarSeverity.Success);
            await LoadAsync();
            TilesReset?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException or ArgumentException)
        {
            ShowFeedback(ErrorText(ex), InfoBarSeverity.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private MapWorldOption? Linked()
    {
        if (_link.Server is not { } server)
        {
            return null;
        }

        var candidates = Worlds.Where(w => w.Server.Id == server || w.Server.NetworkId == server).ToList();
        return candidates.FirstOrDefault(w => _link.World is { } world && w.World.World == world) ?? candidates.FirstOrDefault();
    }

    private void SetPlayers(IReadOnlyList<MapLivePlayer> players)
    {
        var world = SelectedWorld?.World;
        Players.Clear();
        foreach (var p in players.Where(p => world is not null && (p.WorldId == world.Id || (p.WorldId is null && p.World == world.World))))
        {
            Players.Add(new MapPointItem(p.Bedrock ? p.Name + " · Bedrock" : p.Name, Coordinates(p.X, p.Z), p.X, p.Z));
        }

        OnPropertyChanged(nameof(HasPlayers));
        PlayersChanged?.Invoke(this, players);
        UpdateLiveInfo();
    }

    private void UpdateLiveInfo()
    {
        OnPropertyChanged(nameof(LiveAvailable));
        LiveInfo = Overview switch
        {
            null => null,
            { LivePlayers: false, LiveMode: MinecraftService.LiveAuth } => L(StringKeys.MinecraftMapLiveSignIn),
            { LivePlayers: false } => L(StringKeys.MinecraftMapLiveOff),
            _ when !ShowLive => null,
            _ when Players.Count == 0 => L(StringKeys.MinecraftMapNoPlayers),
            _ => null,
        };
    }

    private void ApplyAdmin(MapOverview? map)
    {
        _applyingMode = true;
        SelectedLiveMode = LiveModeOptions.FirstOrDefault(o => o.Value == map?.LiveMode);
        _applyingMode = false;
        QuotaText = CanManage && map?.Quota is { Total: > 0 } q
            ? string.Format(CultureInfo.CurrentCulture, L(StringKeys.MinecraftMapQuota), MinecraftMapGeometry.FormatBytes(q.Used), MinecraftMapGeometry.FormatBytes(q.Total), MinecraftMapGeometry.FormatBytes(q.PerServer))
            : null;
        OnPropertyChanged(nameof(HasQuota));
    }

    private void FillWorldAdmin()
    {
        var option = SelectedWorld;
        EditWorldHidden = option?.World.Hidden ?? false;
        EditServerHidden = option?.Server.Hidden ?? false;
        EditNameDe = option?.World.Names?.GetValueOrDefault("de") ?? string.Empty;
        EditNameEn = option?.World.Names?.GetValueOrDefault("en") ?? string.Empty;
        EditNameFr = option?.World.Names?.GetValueOrDefault("fr") ?? string.Empty;
        WorldAdminInfo = option is null || !CanManage
            ? null
            : string.Format(
                CultureInfo.CurrentCulture,
                L(option.Server.IsPublic == false ? StringKeys.MinecraftMapWorldAdminNotPublic : StringKeys.MinecraftMapWorldAdmin),
                option.World.World,
                MinecraftMapGeometry.FormatBytes(option.World.Bytes ?? 0));
    }

    private static string Coordinates(double x, double z) =>
        string.Format(CultureInfo.CurrentCulture, "x {0:0} · z {1:0}", Math.Floor(x), Math.Floor(z));

    private string L(string key) => services.Localizer.GetString(key);

    private string ErrorText(Exception ex) =>
        services.Localizer.GetError(ex is ApiProblemException p ? p.Problem.Code : ApiProblem.Codes.Unknown);

    private void ShowFeedback(string message, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = message;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }
}
