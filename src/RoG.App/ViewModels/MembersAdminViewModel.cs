using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Admin;
using RoG.Core.Localization;
using RoG.Core.Navigation;

namespace RoG.App.ViewModels;

/// <summary>Verwaltung – Mitglieder: Liste mit Suche/Status, Konto mit Rollen; Rollen vergeben/entziehen (mit Recht).</summary>
public sealed partial class MembersAdminViewModel(AppServices services, Action<string, InfoBarSeverity> show) : ObservableObject
{
    private readonly MemberAdminService _admin = new(services.Api.Client);
    private string? _cursor;
    private int _loadGeneration;
    private bool _rolesLoaded;

    public bool CanView => services.CurrentUser is { } me && me.CanAnywhere("members.view") && me.HasScope("members:read");

    public bool CanAssign => services.CurrentUser is { } me && me.CanAnywhere("roles.assign") && me.HasScope("roles:write");

    public IReadOnlyList<SettingsOption> StatusOptions { get; } = new[] { "active", "suspended", "former" }
        .Select(s => new SettingsOption(s, services.Localizer.GetString("MemberAdmin_Status_" + s))).ToList();

    public ObservableCollection<MemberAdminRowViewModel> Items { get; } = [];

    public ObservableCollection<RoleAssignmentViewModel> Roles { get; } = [];

    public ObservableCollection<SettingsOption> RoleOptions { get; } = [];

    public ObservableCollection<SettingsOption> ScopeOptions { get; } = [];

    [ObservableProperty]
    public partial SettingsOption? SelectedStatus { get; set; }

    [ObservableProperty]
    public partial string? Search { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial MemberAdminRowViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial bool HasDetail { get; set; }

    [ObservableProperty]
    public partial string Header { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AccountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SettingsOption? SelectedRole { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedScope { get; set; }

    /// <summary>Mitglied moderieren (Dialog der Verwaltungs-Seite).</summary>
    public Func<string, Task>? Moderate { get; set; }

    public bool CanModerate { get; set; }

    private AdminMemberDetail? _detail;

    partial void OnSelectedStatusChanged(SettingsOption? value) => _ = LoadAsync(reset: true);

    partial void OnSelectedChanged(MemberAdminRowViewModel? value)
    {
        if (value is not null)
        {
            _ = OpenAsync(value.Slug);
        }
    }

    public async Task LoadAsync(bool reset)
    {
        if (!CanView)
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
            _cursor = null;
            Items.Clear();
        }

        await RunAsync(async () =>
        {
            var page = await _admin.ListAsync(Search, SelectedStatus.Value, _cursor);
            if (generation != _loadGeneration)
            {
                return; // inzwischen neu gefiltert
            }

            foreach (var m in page.Items)
            {
                Items.Add(new MemberAdminRowViewModel(m));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        });
    }

    [RelayCommand]
    private Task MoreAsync() => LoadAsync(reset: false);

    [RelayCommand]
    private Task SearchNowAsync() => LoadAsync(reset: true);

    private async Task OpenAsync(string slug)
    {
        if (CanAssign && !_rolesLoaded)
        {
            _rolesLoaded = true;
            await RunAsync(async () =>
            {
                foreach (var r in await _admin.RolesAsync())
                {
                    RoleOptions.Add(new SettingsOption(r.Id, r.Label));
                }
            });
        }

        await RunAsync(async () =>
        {
            var d = await _admin.DetailAsync(slug);
            _detail = d;
            var l = services.Localizer;
            Header = string.Join(" · ", new[] { d.Name, d.Rank }.Where(s => !string.IsNullOrEmpty(s)));
            AccountText = string.Join("\n", new[]
            {
                l.GetString("MemberAdmin_Status_" + d.AccountStatus),
                d.Email,
                d.TwoFactor is { } tf ? l.GetString(tf ? StringKeys.AccountTwoFactorOn : StringKeys.AccountTwoFactorOff) : null,
                d.JoinedAt is { } j ? string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.MemberAdminJoined), j.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)) : null,
                d.LastSeenAt is { } s ? string.Format(CultureInfo.CurrentCulture, l.GetString(StringKeys.MemberAdminLastSeen), s.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)) : null,
                d.Squads.Count > 0 ? string.Join(", ", d.Squads.Select(q => q.Name)) : null,
            }.Where(x => !string.IsNullOrEmpty(x)));
            Roles.Clear();
            foreach (var r in d.Roles)
            {
                Roles.Add(new RoleAssignmentViewModel(r, d.Squads, this, l));
            }

            ScopeOptions.Clear();
            ScopeOptions.Add(new SettingsOption(string.Empty, l.GetString(StringKeys.MemberAdminScopeGlobal)));
            foreach (var (id, name) in d.Squads)
            {
                ScopeOptions.Add(new SettingsOption(id, name));
            }

            SelectedScope = ScopeOptions[0];
            HasDetail = true;
        });
    }

    [RelayCommand]
    private async Task AssignAsync()
    {
        if (_detail is not { } d || SelectedRole is not { } role)
        {
            return;
        }

        var squad = string.IsNullOrEmpty(SelectedScope?.Value) ? null : SelectedScope.Value;
        if (await RunAsync(() => _admin.AssignAsync(d.Slug, role.Value, squad, null)))
        {
            show(services.Localizer.GetString(StringKeys.MemberAdminAssigned), InfoBarSeverity.Success);
            await OpenAsync(d.Slug);
        }
    }

    internal async Task RevokeAsync(RoleAssignmentViewModel role)
    {
        if (_detail is { } d && await RunAsync(() => _admin.RevokeAsync(d.Slug, role.Id)))
        {
            Roles.Remove(role);
            show(services.Localizer.GetString(StringKeys.MemberAdminRevoked), InfoBarSeverity.Success);
        }
    }

    [RelayCommand]
    private void OpenProfile()
    {
        if (_detail is { } d)
        {
            services.Navigate(new AppRoute(AppSection.Members, d.Slug));
        }
    }

    [RelayCommand]
    private Task ModerateMemberAsync() => _detail is { } d && Moderate is not null ? Moderate(d.Id) : Task.CompletedTask;

    private async Task<bool> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (ApiProblemException ex)
        {
            show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            show(services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }

        return false;
    }
}

public sealed class MemberAdminRowViewModel(AdminMemberRow row)
{
    public string Slug => row.Slug;

    public string Name => row.Name;

    public string Meta { get; } = string.Join(" · ", new[] { row.Rank, string.Join(", ", row.Squads) }.Where(s => !string.IsNullOrEmpty(s)));
}

public sealed partial class RoleAssignmentViewModel(RoleAssignment role, IReadOnlyList<(string Id, string Name)> squads, MembersAdminViewModel owner, ILocalizer localizer)
{
    public string Id => role.AssignmentId;

    public string Text { get; } = string.Join(" · ", new[]
    {
        role.RoleLabel,
        role.ScopeType is "squad" ? squads.FirstOrDefault(s => s.Id == role.ScopeId).Name ?? localizer.GetString(StringKeys.MemberAdminScopeSquad) : role.ScopeType,
        role.ExpiresAt is { } e ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.BanUntil), e.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool CanRevoke => owner.CanAssign;

    [RelayCommand]
    private Task RevokeAsync() => owner.RevokeAsync(this);
}
