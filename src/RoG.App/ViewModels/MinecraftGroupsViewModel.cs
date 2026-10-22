using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Admin;
using RoG.Core.Features.Minecraft;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Verwaltung → Minecraft → Gruppen (Website v0.37.0, Guide 44 „Gruppen (LuckPerms)“): Rang/Zusatzrolle → LuckPerms-Gruppe
/// mit Priorität, verwaltete und entfernte Gruppen, „Nicht mehr verwalten“. Rangschutz, geschützte Gruppen und
/// Owner-Pflicht beim Freigeben prüft der Server – die App zeigt nur dessen Antwort.
/// </summary>
public sealed partial class MinecraftGroupsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<string, InfoBarSeverity> _show;
    private readonly MinecraftService _minecraft;
    private readonly MemberAdminService _roles;
    private bool _loaded;

    public MinecraftGroupsViewModel(AppServices services, Action<string, InfoBarSeverity> show)
    {
        _services = services;
        _show = show;
        _minecraft = new MinecraftService(services.Api.Http);
        _roles = new MemberAdminService(services.Api.Client);
        var me = services.CurrentUser;
        CanView = me is not null && me.HasScope(MinecraftService.ManageScope) && me.Can(MinecraftService.ManagePermission);
    }

    public bool CanView { get; }

    public ObservableCollection<MinecraftGroupItemViewModel> Mappings { get; } = [];

    public ObservableCollection<MinecraftRetiredItemViewModel> Retired { get; } = [];

    /// <summary>Ränge und Zusatzrollen (Squad-Rollen lassen sich nicht zuordnen).</summary>
    public ObservableCollection<SettingsOption> RoleOptions { get; } = [];

    [ObservableProperty]
    public partial SettingsOption? SelectedRole { get; set; }

    [ObservableProperty]
    public partial string NewGroup { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double NewPriority { get; set; }

    [ObservableProperty]
    public partial string ManagedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasRetired { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Entfernen/Freigeben bestätigen lassen (Titel, Text) – setzt die Seite.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await Task.WhenAll(LoadRolesAsync(), LoadAsync());
    }

    [RelayCommand]
    private Task ReloadAsync() => LoadAsync();

    private async Task LoadRolesAsync()
    {
        try
        {
            var roles = await _roles.RolesAsync();
            RoleOptions.Clear();
            foreach (var r in roles.Where(r => r.Type != "squad"))
            {
                RoleOptions.Add(new SettingsOption(r.Id, r.Label));
            }
        }
        catch (ApiProblemException)
        {
            // Ohne roles:read bleibt nur das Bearbeiten vorhandener Zuordnungen.
        }
        catch (System.Text.Json.JsonException)
        {
        }
    }

    private async Task LoadAsync()
    {
        if (!CanView || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Apply(await _minecraft.GroupMappingsAsync());
        }
        catch (ApiProblemException ex)
        {
            _show(_services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (System.Text.Json.JsonException)
        {
            _show(_services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(MinecraftGroupOverview overview)
    {
        Mappings.Clear();
        foreach (var m in overview.Mappings)
        {
            Mappings.Add(new MinecraftGroupItemViewModel(m, this, _services.Localizer));
        }

        Retired.Clear();
        foreach (var r in overview.RetiredGroups)
        {
            Retired.Add(new MinecraftRetiredItemViewModel(r, this, _services.Localizer));
        }

        IsEmpty = Mappings.Count == 0;
        HasRetired = Retired.Count > 0;
        ManagedText = overview.ManagedGroups.Count == 0 ? L(StringKeys.MinecraftGroupsManagedEmpty) : string.Join(", ", overview.ManagedGroups);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedRole is not { } role)
        {
            _show(L(StringKeys.MinecraftGroupsPickRole), InfoBarSeverity.Warning);
            return;
        }

        if (!MinecraftService.IsValidGroup(NewGroup))
        {
            _show(L(StringKeys.MinecraftGroupsFormat), InfoBarSeverity.Warning);
            return;
        }

        var priority = double.IsNaN(NewPriority) ? 0 : (int)Math.Round(NewPriority);
        await RunAsync(async () =>
        {
            await _minecraft.SetGroupAsync(role.Value, NewGroup, priority);
            NewGroup = string.Empty;
            NewPriority = 0;
            SelectedRole = null;
            _show(L(StringKeys.MinecraftGroupsSaved), InfoBarSeverity.Success);
        });
    }

    /// <summary>Vorhandene Zuordnung ins Formular übernehmen.</summary>
    internal void Edit(MinecraftGroupItemViewModel item)
    {
        var option = RoleOptions.FirstOrDefault(o => o.Value == item.RoleId);
        if (option is null)
        {
            option = new SettingsOption(item.RoleId, item.RoleLabel);
            RoleOptions.Add(option);
        }

        SelectedRole = option;
        NewGroup = item.Group;
        NewPriority = item.Priority;
    }

    internal async Task RemoveAsync(MinecraftGroupItemViewModel item)
    {
        if (!await Confirm(L(StringKeys.MinecraftGroupsRemoveTitle), Format(StringKeys.MinecraftGroupsRemoveText, item.RoleLabel)))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _minecraft.RemoveGroupAsync(item.RoleId);
            _show(L(StringKeys.MinecraftGroupsRemoved), InfoBarSeverity.Success);
        });
    }

    internal async Task ReleaseAsync(MinecraftRetiredItemViewModel item)
    {
        if (!await Confirm(L(StringKeys.MinecraftGroupsReleaseTitle), Format(StringKeys.MinecraftGroupsReleaseText, item.Group)))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _minecraft.ReleaseGroupAsync(item.Group);
            _show(L(StringKeys.MinecraftGroupsReleased), InfoBarSeverity.Success);
        });
    }

    /// <summary>Aktion ausführen, danach neu laden – der Server liefert verwaltete/entfernte Gruppen neu.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        catch (ApiProblemException ex)
        {
            _show(_services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException)
        {
            _show(_services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
    }

    private string L(string key) => _services.Localizer.GetString(key);

    private string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, L(key), args);
}

public sealed partial class MinecraftGroupItemViewModel(MinecraftGroupMapping mapping, MinecraftGroupsViewModel owner, ILocalizer localizer)
{
    public string RoleId => mapping.RoleId;

    public string RoleLabel => mapping.RoleLabel;

    public string Group => mapping.Group;

    public int Priority => mapping.Priority;

    public string Meta { get; } = string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.MinecraftGroupsMeta), mapping.Group, mapping.Priority);

    [RelayCommand]
    private void Edit() => owner.Edit(this);

    [RelayCommand]
    private Task RemoveAsync() => owner.RemoveAsync(this);
}

public sealed partial class MinecraftRetiredItemViewModel(MinecraftRetiredGroup group, MinecraftGroupsViewModel owner, ILocalizer localizer)
{
    public string Group => group.Group;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        group.RetiredAt is { } at ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.MinecraftGroupsRetiredSince), at.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)) : null,
        group.ReleasableAt is { } from ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.MinecraftGroupsReleasableFrom), from.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>Vor <c>releasable_at</c> deaktiviert; ob der Nutzer Owner ist, entscheidet der Server (403).</summary>
    public bool CanRelease { get; } = group.IsReleasable(DateTimeOffset.UtcNow);

    [RelayCommand]
    private Task ReleaseAsync() => owner.ReleaseAsync(this);
}
