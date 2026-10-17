using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Members;
using RoG.Core.Features.Squads;
using RoG.Core.Features.Uploads;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Squad verwalten (eigenes Fenster): Texte und Rekrutierung in der App-Sprache, Logo/Header, Line-up mit Rollen und
/// Ersatz-Kennzeichen, Mitglied hinzufügen, archivieren. Was erlaubt ist, kommt aus <see cref="SquadAdminRights"/>;
/// den Einzelfall (Hierarchie) entscheidet der Server.
/// </summary>
public sealed partial class SquadAdminViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly SquadService _squads;
    private readonly SquadAdminService _admin;
    private readonly MemberService _members;
    private readonly UploadService _uploads;
    private SquadDetail _current;

    public SquadAdminViewModel(AppServices services, SquadDetail current, SquadAdminRights rights)
    {
        _services = services;
        _current = current;
        Rights = rights;
        _squads = new SquadService(services.Api.Client);
        _admin = new SquadAdminService(services.Api.Http);
        _members = new MemberService(services.Api.Client);
        _uploads = new UploadService(services.Api.Client, services.StorageHttp);
        RecruitingOptions = SquadAdminService.RecruitingValues
            .Select(v => new SettingsOption(v, services.Localizer.GetString("SquadAdmin_Recruiting_" + v))).ToList();
        FillForm();
    }

    public SquadAdminRights Rights { get; }

    public string Title => _current.Summary.Name;

    public bool CanEdit => Rights.Edit;

    public bool CanMembers => Rights.Members;

    public bool CanArchive => Rights.Archive;

    /// <summary>Etwas wurde geändert – die Squad-Seite lädt nach dem Schließen neu.</summary>
    public bool Changed { get; private set; }

    public IReadOnlyList<SettingsOption> RecruitingOptions { get; }

    public ObservableCollection<SquadRole> Roles { get; } = [];

    public ObservableCollection<SquadAdminMemberViewModel> Lineup { get; } = [];

    public ObservableCollection<MemberSummary> Suggestions { get; } = [];

    [ObservableProperty]
    public partial string Tagline { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TrainingTimes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClanRequirements { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClanTag { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClanName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double ClanLevel { get; set; } = double.NaN;

    [ObservableProperty]
    public partial SettingsOption? SelectedRecruiting { get; set; }

    [ObservableProperty]
    public partial MemberSummary? NewMember { get; set; }

    [ObservableProperty]
    public partial SquadRole? NewMemberRole { get; set; }

    [ObservableProperty]
    public partial bool? NewMemberSubstitute { get; set; } = false;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    public string ArchiveLabel => L(_current.Archived ? StringKeys.SquadAdminRestore : StringKeys.SquadAdminArchive);

    /// <summary>Rückfrage (Titel, Text) – setzt das Fenster.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);

    private string L(string key) => _services.Localizer.GetString(key);

    private void FillForm()
    {
        Tagline = _current.Summary.Tagline ?? string.Empty;
        Description = _current.Description ?? string.Empty;
        TrainingTimes = _current.TrainingTimes ?? string.Empty;
        ClanRequirements = _current.ClanRequirements ?? string.Empty;
        ClanTag = _current.Summary.ClanTag ?? string.Empty;
        ClanName = _current.ClanName ?? string.Empty;
        ClanLevel = _current.ClanLevel ?? double.NaN;
        var value = SquadAdminService.ValueOf(_current.Summary.Recruiting);
        SelectedRecruiting = RecruitingOptions.FirstOrDefault(o => o.Value == value);
    }

    public async Task LoadMembersAsync()
    {
        if (!CanMembers)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var roles = await _admin.RolesAsync(_current.Summary.Slug);
            Roles.Clear();
            foreach (var r in roles)
            {
                Roles.Add(r);
            }

            NewMemberRole = Roles.LastOrDefault(r => r.Assignable);
            var lineup = await _squads.GetMembersAsync(_current.Summary.Slug);
            Lineup.Clear();
            foreach (var m in lineup)
            {
                Lineup.Add(new SquadAdminMemberViewModel(m, Roles, this));
            }
        });
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        var edit = new SquadEdit(
            Tagline,
            Description,
            TrainingTimes,
            ClanRequirements,
            SelectedRecruiting?.Value,
            ClanTag,
            ClanName,
            double.IsNaN(ClanLevel) ? null : (int)ClanLevel);
        var saved = false;
        if (await RunAsync(async () => saved = await _admin.UpdateAsync(_current, edit, _services.Language)))
        {
            Show(L(saved ? StringKeys.SquadAdminSaved : StringKeys.SquadAdminNothing), saved ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
            if (saved)
            {
                Changed = true;
                await RunAsync(async () => _current = await _squads.GetAsync(_current.Summary.Slug));
                FillForm();
            }
        }
    }

    /// <summary>Logo oder Header: Bild öffentlich hochladen, dann am Squad setzen (Verarbeitung dauert einen Moment).</summary>
    public async Task SetImageAsync(SquadImage kind, Stream content, string fileName, long size)
    {
        if (UploadService.MimeFor(fileName) is not { } mime || !mime.StartsWith("image/", StringComparison.Ordinal))
        {
            Show(L(StringKeys.WikiImageOnly), InfoBarSeverity.Warning);
            return;
        }

        IsBusy = true;
        try
        {
            if (await RunAsync(async () =>
            {
                var upload = await _uploads.UploadAsync(content, fileName, size, UploadVisibility.Public);
                await _admin.SetImageAsync(_current.Summary.Slug, kind, upload.Id);
            }))
            {
                Changed = true;
                Show(L(StringKeys.SquadAdminImageQueued), InfoBarSeverity.Success);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RemoveImageAsync(SquadImage kind)
    {
        if (await Confirm(L(kind == SquadImage.Logo ? StringKeys.SquadAdminRemoveLogo : StringKeys.SquadAdminRemoveHeader), Title)
            && await RunAsync(() => _admin.RemoveImageAsync(_current.Summary.Slug, kind)))
        {
            Changed = true;
            Show(L(StringKeys.SquadAdminSaved), InfoBarSeverity.Success);
        }
    }

    /// <summary>Vorschläge für „Mitglied hinzufügen“ (ab 2 Zeichen).</summary>
    public async Task SearchMembersAsync(string text)
    {
        Suggestions.Clear();
        if (text.Trim().Length < 2)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var page = await _members.ListAsync(text, onlineOnly: false);
            foreach (var m in page.Items.Take(10))
            {
                Suggestions.Add(m);
            }
        });
    }

    [RelayCommand]
    private async Task AddMemberAsync()
    {
        if (NewMember is not { } member || NewMemberRole is not { } role)
        {
            Show(L(StringKeys.SquadAdminPickMember), InfoBarSeverity.Warning);
            return;
        }

        if (await RunAsync(() => _admin.PutMemberAsync(_current.Summary.Slug, member.Slug, role.Id, NewMemberSubstitute == true)))
        {
            Changed = true;
            NewMember = null;
            Show(string.Format(CultureInfo.CurrentCulture, L(StringKeys.SquadAdminMemberAdded), member.Name), InfoBarSeverity.Success);
            await LoadMembersAsync();
        }
    }

    internal async Task SaveMemberAsync(SquadAdminMemberViewModel item)
    {
        if (item.SelectedRole is not { } role)
        {
            return;
        }

        if (await RunAsync(() => _admin.PutMemberAsync(_current.Summary.Slug, item.Slug, role.Id, item.Substitute == true)))
        {
            Changed = true;
            Show(string.Format(CultureInfo.CurrentCulture, L(StringKeys.SquadAdminMemberSaved), item.Name), InfoBarSeverity.Success);
        }
    }

    internal async Task RemoveMemberAsync(SquadAdminMemberViewModel item)
    {
        if (await Confirm(L(StringKeys.SquadAdminRemoveMemberTitle), item.Name)
            && await RunAsync(() => _admin.RemoveMemberAsync(_current.Summary.Slug, item.Slug)))
        {
            Changed = true;
            Lineup.Remove(item);
        }
    }

    [RelayCommand]
    private async Task ToggleArchiveAsync()
    {
        var archive = !_current.Archived;
        if (!await Confirm(ArchiveLabel, L(archive ? StringKeys.SquadAdminArchiveText : StringKeys.SquadAdminRestoreText)))
        {
            return;
        }

        if (await RunAsync(() => _admin.SetArchivedAsync(_current.Summary.Slug, archive)))
        {
            Changed = true;
            _current = _current with { Archived = archive };
            OnPropertyChanged(nameof(ArchiveLabel));
            Show(L(archive ? StringKeys.SquadAdminArchived : StringKeys.SquadAdminRestored), InfoBarSeverity.Success);
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
            Show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : _services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            Show(_services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }

        return false;
    }

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }
}

/// <summary>Zeile im Line-up: Rolle wählen (nur vergebbare), Ersatz an/aus, speichern, entfernen.</summary>
public sealed partial class SquadAdminMemberViewModel : ObservableObject
{
    private readonly SquadAdminViewModel _owner;

    public SquadAdminMemberViewModel(SquadMember member, IEnumerable<SquadRole> roles, SquadAdminViewModel owner)
    {
        _owner = owner;
        Slug = member.Slug;
        Name = member.Name;
        Roles = roles.ToList();
        SelectedRole = Roles.FirstOrDefault(r => string.Equals(r.Label, member.RoleLabel, StringComparison.OrdinalIgnoreCase));
        Substitute = member.Substitute;
    }

    public string Slug { get; }

    public string Name { get; }

    public IReadOnlyList<SquadRole> Roles { get; }

    [ObservableProperty]
    public partial SquadRole? SelectedRole { get; set; }

    [ObservableProperty]
    public partial bool? Substitute { get; set; }

    [RelayCommand]
    private Task SaveAsync() => _owner.SaveMemberAsync(this);

    [RelayCommand]
    private Task RemoveAsync() => _owner.RemoveMemberAsync(this);
}
