using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Admin;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Verwaltung – Übersicht: Kennzahlen, Systemstatus (Ampel), Audit-Log. Jede Quelle nur mit Recht + Scope.</summary>
public sealed partial class AdminOverviewViewModel(AppServices services, Action<string, InfoBarSeverity> show) : ObservableObject
{
    private readonly AdminOverviewService _overview = new(services.Api.Http);
    private string? _auditCursor;

    public bool CanKpis => Allowed("admin.access", "admin:read");

    public bool CanSystem => Allowed("system.view", "system:read");

    public bool CanAudit => Allowed("audit.view", "audit:read");

    public bool Any => CanKpis || CanSystem || CanAudit;

    public ObservableCollection<Kpi> Kpis { get; } = [];

    public ObservableCollection<SystemCheckViewModel> Checks { get; } = [];

    public ObservableCollection<string> Audit { get; } = [];

    [ObservableProperty]
    public partial string SystemText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Brush? SystemBrush { get; set; }

    [ObservableProperty]
    public partial string? AuditSearch { get; set; }

    [ObservableProperty]
    public partial bool HasMoreAudit { get; set; }

    public async Task LoadAsync()
    {
        if (CanKpis)
        {
            await RunAsync(async () =>
            {
                Kpis.Clear();
                foreach (var k in await _overview.KpisAsync())
                {
                    Kpis.Add(k);
                }
            });
        }

        if (CanSystem)
        {
            await RunAsync(async () =>
            {
                Checks.Clear();
                if (await _overview.SystemAsync() is not { } system)
                {
                    SystemText = services.Localizer.GetString(StringKeys.AdminSystemNever);
                    return;
                }

                SystemText = string.Join(" · ", new[]
                {
                    services.Localizer.GetString("Admin_System_" + system.Status),
                    system.Version is { } v ? "v" + v : null,
                    system.CheckedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                }.Where(s => !string.IsNullOrEmpty(s)));
                SystemBrush = (Brush)Application.Current.Resources[system.Status switch
                {
                    "ok" => "RoGStatusOnlineBrush",
                    "down" => "RoGRedBrush",
                    _ => "RoGGoldBrush",
                }];
                foreach (var c in system.Checks.OrderBy(c => c.Ok switch { false => 0, null => 2, _ => 1 }))
                {
                    Checks.Add(new SystemCheckViewModel(c));
                }
            });
        }

        if (CanAudit)
        {
            await LoadAuditAsync(reset: true);
        }
    }

    public async Task LoadAuditAsync(bool reset)
    {
        if (reset)
        {
            _auditCursor = null;
            Audit.Clear();
        }

        await RunAsync(async () =>
        {
            var page = await _overview.AuditAsync(AuditSearch, _auditCursor);
            foreach (var e in page.Items)
            {
                Audit.Add(string.Join(" · ", new[]
                {
                    e.CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    e.Actor,
                    e.Action,
                    e.Subject,
                }.Where(s => !string.IsNullOrEmpty(s))));
            }

            _auditCursor = page.NextCursor;
            HasMoreAudit = page.HasMore;
        });
    }

    [RelayCommand]
    private Task MoreAuditAsync() => LoadAuditAsync(reset: false);

    [RelayCommand]
    private Task SearchAuditAsync() => LoadAuditAsync(reset: true);

    [RelayCommand]
    private Task OpenKpiAsync(Kpi kpi) => kpi.Url is { } url ? services.Browser.OpenAsync(url) : Task.CompletedTask;

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiProblemException ex)
        {
            show(services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            show(services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    private bool Allowed(string permission, string scope) =>
        services.CurrentUser is { } me && me.CanAnywhere(permission) && me.HasScope(scope);
}

public sealed class SystemCheckViewModel(SystemCheck check)
{
    public string Icon { get; } = check.Ok switch { true => "✓", false => "✗", null => "–" };

    public Brush IconBrush { get; } = (Brush)Application.Current.Resources[check.Ok switch
    {
        true => "RoGStatusOnlineBrush",
        false => "RoGRedBrush",
        null => "RoGMutedBrush",
    }];

    public string Label { get; } = check.Critical ? check.Label + " *" : check.Label;

    public string Value { get; } = check.Value ?? string.Empty;

    public string Help { get; } = check.Help ?? string.Empty;

    public bool HasHelp => !string.IsNullOrEmpty(check.Help);
}
