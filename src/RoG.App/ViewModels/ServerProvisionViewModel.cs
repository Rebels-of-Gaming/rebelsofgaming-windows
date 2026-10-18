using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Admin;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// „Neuer Gameserver“ (Pterodactyl über die Website): Node und Egg wählen, Formular nach Egg (Variablen mit Regeln),
/// Limits, Website-Felder, Ablauf. Nach dem Anlegen bzw. für einen bestehenden, über die Website angelegten Server:
/// Installationsstand, Ablauf ändern/verlängern, Löschen in der Gefahrenzone (Bestätigung mit dem Servernamen).
/// </summary>
public sealed partial class ServerProvisionViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly ServerProvisioningService _provisioning;
    private readonly AdminResourceService _resources;
    private ProvisionCatalog? _catalog;
    private AdminArea? _area;

    public ServerProvisionViewModel(AppServices services, GameServer? existing)
    {
        _services = services;
        _provisioning = new ServerProvisioningService(services.Api.Http);
        _resources = new AdminResourceService(services.Api.Http);
        if (existing is not null)
        {
            ShowManage(existing.Slug, existing.Name, existing.Provisioning);
        }
    }

    /// <summary>Server angelegt oder gelöscht – Serverliste neu laden.</summary>
    public event EventHandler? Changed;

    /// <summary>Rückfrage (Titel, Text) → bestätigt.</summary>
    public Func<string, string, Task<bool>>? Confirm { get; set; }

    public ObservableCollection<SettingsOption> Nodes { get; } = [];

    public ObservableCollection<SettingsOption> Eggs { get; } = [];

    public ObservableCollection<AdminFieldViewModel> Fields { get; } = [];

    public ObservableCollection<SettingsOption> ExpiryOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCreate), nameof(ShowManagePanel))]
    public partial bool IsManaging { get; set; }

    public bool ShowCreate => !IsManaging && IsCatalogReady;

    public bool ShowManagePanel => IsManaging;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCreate))]
    public partial bool IsCatalogReady { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedNode { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedEgg { get; set; }

    [ObservableProperty]
    public partial string? NodeInfo { get; set; }

    [ObservableProperty]
    public partial string? EggInfo { get; set; }

    [ObservableProperty]
    public partial string? Feedback { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    // ---- Verwaltung eines angelegten Servers ----

    private string? _slug;

    [ObservableProperty]
    public partial string? ServerName { get; set; }

    [ObservableProperty]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    public partial string? ExpiryText { get; set; }

    [ObservableProperty]
    public partial bool HasExpiry { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedExpiry { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    public partial string ConfirmName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    public partial bool Deletable { get; set; }

    public bool CanDelete => Deletable && ServerName is { } name && ServerProvisioningService.ConfirmMatches(name, ConfirmName.Trim());

    public string Title => _services.Localizer.GetString(IsManaging ? StringKeys.ProvisionManageTitle : StringKeys.ProvisionTitle);

    /// <summary>Katalog laden (nur beim Anlegen) und Ablauf-Vorgaben füllen.</summary>
    public async Task LoadAsync()
    {
        if (IsManaging)
        {
            FillExpiryOptions([1, 3, 7, 14, 30]);
            return;
        }

        await RunAsync(async () =>
        {
            _catalog = await _provisioning.CatalogAsync();
            Nodes.Clear();
            foreach (var n in _catalog.Nodes)
            {
                Nodes.Add(new SettingsOption(n.Id.ToString(CultureInfo.InvariantCulture), $"{n.Name} · {_catalog.LocationOf(n)}"));
            }

            Eggs.Clear();
            foreach (var e in _catalog.Eggs.OrderBy(e => e.NestName, StringComparer.CurrentCulture).ThenBy(e => e.Name, StringComparer.CurrentCulture))
            {
                Eggs.Add(new SettingsOption(e.Id.ToString(CultureInfo.InvariantCulture), e.Display));
            }

            FillExpiryOptions(_catalog.ExpiryPresets);
            SelectedNode = Nodes.FirstOrDefault();
            IsCatalogReady = true;
            await RebuildAsync();
        }, success: null);
    }

    private void FillExpiryOptions(IEnumerable<int> presets)
    {
        ExpiryOptions.Clear();
        foreach (var d in presets)
        {
            ExpiryOptions.Add(new SettingsOption(d.ToString(CultureInfo.InvariantCulture), _services.Localizer.GetString("Provision_Days_" + d.ToString(CultureInfo.InvariantCulture))));
        }

        SelectedExpiry = ExpiryOptions.FirstOrDefault(o => o.Value == "7") ?? ExpiryOptions.FirstOrDefault();
    }

    private ProvisionNode? Node => _catalog?.Nodes.FirstOrDefault(n => n.Id.ToString(CultureInfo.InvariantCulture) == SelectedNode?.Value);

    private ProvisionEgg? Egg => _catalog?.Eggs.FirstOrDefault(e => e.Id.ToString(CultureInfo.InvariantCulture) == SelectedEgg?.Value);

    partial void OnSelectedNodeChanged(SettingsOption? value) => _ = RebuildAsync();

    partial void OnSelectedEggChanged(SettingsOption? value) => _ = RebuildAsync();

    /// <summary>Formular neu aufbauen (Node/Egg gewechselt) – allgemeine Eingaben bleiben erhalten.</summary>
    private async Task RebuildAsync()
    {
        if (_catalog is null)
        {
            return;
        }

        var l = _services.Localizer;
        var node = Node;
        var egg = Egg;
        NodeInfo = node is null ? null : string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.ProvisionNodeInfo),
            node.Fqdn, node.MemoryMb / 1024, node.DiskMb / 1024, node.Allocations.Count);
        EggInfo = egg?.Description;
        var keep = Fields.Count > 0 ? Current() : null;
        var values = ProvisionForm.Defaults(egg, keep);
        _area = ProvisionForm.Area(_catalog, node, egg);
        Fields.Clear();
        foreach (var f in _area.Fields)
        {
            Fields.Add(new AdminFieldViewModel(f, values.GetValueOrDefault(f.Key), l, enabled: true, _services.Language));
        }

        foreach (var field in Fields.Where(f => f.Field.Type == AdminFieldType.Reference && f.Field.Reference is not null))
        {
            if (AdminAreas.Find(field.Field.Reference!) is { } refArea)
            {
                await field.LoadReferenceAsync(_resources, refArea, _services.Language);
            }
        }
    }

    private AdminFormValues Current()
    {
        var values = new AdminFormValues();
        foreach (var f in Fields)
        {
            values[f.Field.Key] = f.Value;
        }

        return values;
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (_area is null || Node is not { } node || Egg is not { } egg)
        {
            Show(_services.Localizer.GetString(StringKeys.ProvisionChooseNodeEgg), InfoBarSeverity.Warning);
            return;
        }

        var values = Current();
        var errors = ProvisionForm.Validate(_area, egg, values);
        foreach (var f in Fields)
        {
            f.Error = errors.TryGetValue(f.Field.Key, out var e) ? ErrorText(e) : null;
        }

        if (errors.Count > 0)
        {
            Show(_services.Localizer.GetString(StringKeys.AdminFormCheckFields), InfoBarSeverity.Warning);
            return;
        }

        var name = values.Text("name").Trim();
        var l = _services.Localizer;
        if (Confirm is not null && !await Confirm(l.GetString(StringKeys.ProvisionConfirmTitle), string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.ProvisionConfirmText), name, node.Name, egg.Display)))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var (_, slug, provisioning) = await _provisioning.ProvisionAsync(ProvisionForm.Body(_area, node, egg, values));
            Changed?.Invoke(this, EventArgs.Empty);
            ShowManage(slug, name, provisioning);
        }, success: StringKeys.ProvisionCreated);
    }

    private string ErrorText(string error)
    {
        // „Schlüssel|Wert“ aus den Egg-Regeln, sonst Fehler des allgemeinen Editors
        var parts = error.Split('|', 2);
        var text = _services.Localizer.GetString(parts[0]);
        return parts.Length == 2 ? string.Format(CultureInfo.CurrentCulture, text, parts[1]) : text;
    }

    private void ShowManage(string slug, string name, ServerProvisioning? provisioning)
    {
        _slug = slug;
        ServerName = name;
        IsManaging = true;
        Apply(provisioning);
        OnPropertyChanged(nameof(Title));
        if (ExpiryOptions.Count == 0)
        {
            FillExpiryOptions(_catalog?.ExpiryPresets ?? [1, 3, 7, 14, 30]);
        }
    }

    private void Apply(ServerProvisioning? p)
    {
        var l = _services.Localizer;
        StatusText = p is null ? l.GetString(StringKeys.ProvisionStatusUnknown) : l.GetString("Provision_Status_" + p.Status) is var t && !t.StartsWith("Provision_Status_", StringComparison.Ordinal) ? t : p.Status;
        HasExpiry = p?.ExpiresAt is not null;
        ExpiryText = p?.ExpiresAt is { } at
            ? string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.ProvisionExpiresAt), at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : l.GetString(StringKeys.ProvisionNoExpiry);
        Deletable = p?.Deletable == true;
    }

    [RelayCommand]
    private Task RefreshStatusAsync() => _slug is not { } slug ? Task.CompletedTask : RunAsync(async () => Apply(await _provisioning.RefreshAsync(slug)), success: null);

    [RelayCommand]
    private Task SetExpiryAsync() => ExpiryDays() is not { } days || _slug is not { } slug ? Task.CompletedTask
        : RunAsync(async () => Apply(await _provisioning.SetExpiryAsync(slug, inDays: days)), success: StringKeys.ProvisionExpirySaved);

    [RelayCommand]
    private Task ExtendExpiryAsync() => ExpiryDays() is not { } days || _slug is not { } slug ? Task.CompletedTask
        : RunAsync(async () => Apply(await _provisioning.SetExpiryAsync(slug, extendDays: days)), success: StringKeys.ProvisionExpirySaved);

    [RelayCommand]
    private Task RemoveExpiryAsync() => _slug is not { } slug ? Task.CompletedTask
        : RunAsync(async () => Apply(await _provisioning.SetExpiryAsync(slug)), success: StringKeys.ProvisionExpirySaved);

    private int? ExpiryDays() => int.TryParse(SelectedExpiry?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : null;

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!CanDelete || _slug is not { } slug || ServerName is not { } name)
        {
            return;
        }

        var l = _services.Localizer;
        if (Confirm is null || !await Confirm(l.GetString(StringKeys.ProvisionDeleteTitle), string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.ProvisionDeleteText), name)))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _provisioning.DeleteAsync(slug, ConfirmName.Trim());
            Deletable = false;
            ConfirmName = string.Empty;
            StatusText = l.GetString(StringKeys.ProvisionDeleted);
            HasExpiry = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }, success: StringKeys.ProvisionDeleted);
    }

    private async Task RunAsync(Func<Task> action, string? success)
    {
        IsBusy = true;
        try
        {
            await action();
            if (success is not null)
            {
                Show(_services.Localizer.GetString(success), InfoBarSeverity.Success);
            }
        }
        catch (ApiProblemException ex)
        {
            // Feldfehler der API (auch environment.<VARIABLE>) direkt am Feld zeigen
            foreach (var f in Fields)
            {
                if (ex.Problem.Errors.TryGetValue(f.Field.Key, out var messages) && messages.Count > 0)
                {
                    f.Error = string.Join(" ", messages);
                }
            }

            // Panel-Meldung bzw. Prüfung: Text der API ist aussagekräftiger als der allgemeine
            Show(ex.Problem.Code is "validation_failed" or "provisioning_rejected" && ex.Problem.Detail is { } d ? d : _services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
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
