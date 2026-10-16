using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Control;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Gemeinsame Fehlerbehandlung der Reiter im Steuer-Fenster.</summary>
public abstract class ServerTabViewModel(AppServices services, string slug, Action<string, InfoBarSeverity> show) : ObservableObject
{
    protected AppServices Services { get; } = services;

    protected string Slug { get; } = slug;

    /// <summary>Rückfrage vor Gefährlichem (Titel, Text) – setzt das Fenster.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);

    protected void Show(string text, InfoBarSeverity severity) => show(text, severity);

    protected string L(string key) => Services.Localizer.GetString(key);

    protected async Task<bool> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }

        return false;
    }
}

/// <summary>Config-Editor: Datei der Whitelist wählen, bearbeiten, prüfen (Diff), speichern, Versionen zurückspielen.</summary>
public sealed partial class ServerConfigsViewModel(AppServices services, string slug, ServerAbilities abilities, Action<string, InfoBarSeverity> show)
    : ServerTabViewModel(services, slug, show)
{
    private readonly ServerConfigService _configs = new(services.Api.Client, services.Api.Http);
    private bool _loaded;

    public ObservableCollection<ConfigFile> Files { get; } = [];

    public ObservableCollection<ConfigVersionItemViewModel> Versions { get; } = [];

    public IReadOnlyList<SettingsOption> AfterOptions { get; } =
    [
        new("save", services.Localizer.GetString(StringKeys.ConfigAfterSave)),
        new("exec", services.Localizer.GetString(StringKeys.ConfigAfterExec)),
        new("restart", services.Localizer.GetString(StringKeys.ConfigAfterRestart)),
    ];

    public bool CanWrite => abilities.FilesWrite;

    public bool IsReadOnly => !abilities.FilesWrite;

    [ObservableProperty]
    public partial SettingsOption? SelectedAfter { get; set; }

    [ObservableProperty]
    public partial ConfigFile? SelectedFile { get; set; }

    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DiffText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasDiff { get; set; }

    [ObservableProperty]
    public partial string ErrorsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasErrors { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        SelectedAfter = AfterOptions[0];
        await RunAsync(async () =>
        {
            var files = await _configs.ListAsync(Slug);
            Files.Clear();
            foreach (var f in files)
            {
                Files.Add(f);
            }

            IsEmpty = Files.Count == 0;
            SelectedFile = Files.FirstOrDefault();
        });
    }

    partial void OnSelectedFileChanged(ConfigFile? value) => _ = LoadFileAsync(value);

    private async Task LoadFileAsync(ConfigFile? file)
    {
        Content = string.Empty;
        ClearPreview();
        Versions.Clear();
        if (file is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await RunAsync(async () => Content = await _configs.ReadAsync(Slug, file.Path));
            await RunAsync(async () =>
            {
                foreach (var v in await _configs.VersionsAsync(Slug, file.Id))
                {
                    Versions.Add(new ConfigVersionItemViewModel(v, this));
                }
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (SelectedFile is not { } file)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var preview = await _configs.PreviewAsync(Slug, file.Id, Content);
            DiffText = preview.Changed ? string.Join("\n", preview.Diff.Select(d => d.Display)) : L(StringKeys.ConfigUnchanged);
            HasDiff = true;
            ErrorsText = string.Join("\n", preview.Errors);
            HasErrors = preview.Errors.Count > 0;
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedFile is not { } file)
        {
            return;
        }

        var after = Enum.Parse<ConfigAfter>(SelectedAfter?.Value ?? "save", ignoreCase: true);
        await RunAsync(async () =>
        {
            var outcome = await _configs.SaveAsync(Slug, file.Id, Content, after, Note);
            Show(L(outcome == ConfigSaveOutcome.Pending ? StringKeys.ConfigPending : StringKeys.ConfigSaved), InfoBarSeverity.Success);
            Note = string.Empty;
            await LoadFileAsync(file);
        });
    }

    [RelayCommand]
    private Task ReloadAsync() => LoadFileAsync(SelectedFile);

    internal async Task RollbackAsync(ConfigVersionItemViewModel version)
    {
        if (SelectedFile is not { } file || !await Confirm(L(StringKeys.ConfigRollbackTitle), version.Text))
        {
            return;
        }

        var after = Enum.Parse<ConfigAfter>(SelectedAfter?.Value ?? "save", ignoreCase: true);
        if (await RunAsync(() => _configs.RollbackAsync(Slug, version.Id, after)))
        {
            Show(L(StringKeys.ConfigSaved), InfoBarSeverity.Success);
            await LoadFileAsync(file);
        }
    }

    private void ClearPreview()
    {
        DiffText = string.Empty;
        HasDiff = false;
        ErrorsText = string.Empty;
        HasErrors = false;
    }
}

public sealed partial class ConfigVersionItemViewModel(ConfigVersion version, ServerConfigsViewModel owner)
{
    public string Id => version.Id;

    public string Text { get; } = string.Join(" · ", new[]
    {
        version.CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        version.Author,
        version.Note,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool CanWrite => owner.CanWrite;

    [RelayCommand]
    private Task RollbackAsync() => owner.RollbackAsync(this);
}

/// <summary>Backups: Liste, anlegen, herunterladen (signierter Link im Browser), sperren, wiederherstellen, löschen.</summary>
public sealed partial class ServerBackupsViewModel(AppServices services, string slug, Action<string, InfoBarSeverity> show)
    : ServerTabViewModel(services, slug, show)
{
    private readonly ServerBackupService _backups = new(services.Api.Client);
    private bool _loaded;

    public ObservableCollection<BackupItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial string NewName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public async Task EnsureLoadedAsync()
    {
        if (!_loaded)
        {
            _loaded = true;
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private Task ReloadAsync() => RunAsync(async () =>
    {
        var list = await _backups.ListAsync(Slug);
        Items.Clear();
        foreach (var b in list)
        {
            Items.Add(new BackupItemViewModel(b, this, Services.Localizer));
        }

        IsEmpty = Items.Count == 0;
    });

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (await RunAsync(() => _backups.CreateAsync(Slug, NewName)))
        {
            NewName = string.Empty;
            Show(L(StringKeys.BackupCreated), InfoBarSeverity.Success);
            await ReloadAsync();
        }
    }

    internal Task DownloadAsync(BackupItemViewModel item) => RunAsync(async () =>
        await Services.Browser.OpenAsync(await _backups.DownloadUrlAsync(Slug, item.Uuid)));

    internal async Task RestoreAsync(BackupItemViewModel item)
    {
        if (await Confirm(L(StringKeys.BackupRestoreTitle), string.Format(CultureInfo.CurrentCulture, L(StringKeys.BackupRestoreText), item.Name))
            && await RunAsync(() => _backups.RestoreAsync(Slug, item.Uuid, truncate: false)))
        {
            Show(L(StringKeys.BackupRestoreStarted), InfoBarSeverity.Success);
        }
    }

    internal async Task ToggleLockAsync(BackupItemViewModel item)
    {
        if (await RunAsync(() => _backups.SetLockedAsync(Slug, item.Uuid, !item.Locked)))
        {
            await ReloadAsync();
        }
    }

    internal async Task DeleteAsync(BackupItemViewModel item)
    {
        if (await Confirm(L(StringKeys.BackupDeleteTitle), item.Name) && await RunAsync(() => _backups.DeleteAsync(Slug, item.Uuid)))
        {
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
        }
    }
}

public sealed partial class BackupItemViewModel(ServerBackup backup, ServerBackupsViewModel owner, ILocalizer localizer)
{
    public string Uuid => backup.Uuid;

    public string Name => backup.Name;

    public bool Locked => backup.Locked;

    public bool IsReady => backup.Completed && backup.Successful;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        backup.CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        backup.Completed
            ? (backup.Successful ? $"{backup.Bytes / 1048576.0:0.0} MB" : localizer.GetString(StringKeys.BackupFailed))
            : localizer.GetString(StringKeys.BackupRunning),
        backup.Locked ? localizer.GetString(StringKeys.BackupLocked) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string LockLabel { get; } = localizer.GetString(backup.Locked ? StringKeys.BackupUnlock : StringKeys.BackupLock);

    [RelayCommand]
    private Task DownloadAsync() => owner.DownloadAsync(this);

    [RelayCommand]
    private Task RestoreAsync() => owner.RestoreAsync(this);

    [RelayCommand]
    private Task ToggleLockAsync() => owner.ToggleLockAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => owner.DeleteAsync(this);
}

/// <summary>RCON: freier Befehl, Ausgabe anzeigen (wird nicht gespeichert – kann Spieler-IPs enthalten).</summary>
public sealed partial class ServerRconViewModel(AppServices services, string slug, Action<string, InfoBarSeverity> show)
    : ServerTabViewModel(services, slug, show)
{
    private readonly ServerModerationService _moderation = new(services.Api.Client, services.Api.Http);

    public CommandHistory History { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Command { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Output { get; set; } = string.Empty;

    private bool CanSend() => !string.IsNullOrWhiteSpace(Command) && Command.Trim().Length <= 500;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var command = Command.Trim();
        History.Add(command);
        Command = string.Empty;
        await RunAsync(async () =>
        {
            var output = await _moderation.RconAsync(Slug, command);
            Output = $"> {command}\n{output}\n\n{Output}".TrimEnd();
        });
    }
}

/// <summary>Aktive Sperren der Website, Gast sperren (ID oder Name), Sperre aufheben.</summary>
public sealed partial class ServerBansViewModel(AppServices services, string slug, Action<string, InfoBarSeverity> show)
    : ServerTabViewModel(services, slug, show)
{
    private readonly ServerModerationService _moderation = new(services.Api.Client, services.Api.Http);
    private bool _loaded;

    public ObservableCollection<BanItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial string PlayerId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PlayerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double Minutes { get; set; } = 60;

    [ObservableProperty]
    public partial string Reason { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public async Task EnsureLoadedAsync()
    {
        if (!_loaded)
        {
            _loaded = true;
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private Task ReloadAsync() => RunAsync(async () =>
    {
        var bans = await _moderation.BansAsync(Slug);
        Items.Clear();
        foreach (var b in bans)
        {
            Items.Add(new BanItemViewModel(b, this, Services.Localizer));
        }

        IsEmpty = Items.Count == 0;
    });

    [RelayCommand]
    private async Task BanAsync()
    {
        if (string.IsNullOrWhiteSpace(PlayerId) && string.IsNullOrWhiteSpace(PlayerName))
        {
            Show(L(StringKeys.BanPlayerMissing), InfoBarSeverity.Warning);
            return;
        }

        var minutes = double.IsNaN(Minutes) ? 0 : (int)Math.Max(0, Minutes);
        if (await RunAsync(() => _moderation.BanAsync(Slug, PlayerId, PlayerName, minutes, Reason)))
        {
            PlayerId = PlayerName = Reason = string.Empty;
            Show(L(StringKeys.BanDone), InfoBarSeverity.Success);
            await ReloadAsync();
        }
    }

    internal async Task LiftAsync(BanItemViewModel item)
    {
        if (await Confirm(L(StringKeys.BanLiftTitle), item.Player) && await RunAsync(() => _moderation.LiftAsync(Slug, item.Id)))
        {
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
        }
    }
}

public sealed partial class BanItemViewModel(ServerBan ban, ServerBansViewModel owner, ILocalizer localizer)
{
    public string Id => ban.Id;

    public string Player => ban.Player;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        ban.Reason,
        ban.EndsAt is { } e
            ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.BanUntil), e.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : localizer.GetString(StringKeys.BanPermanent),
        ban.IssuedBy,
    }.Where(s => !string.IsNullOrEmpty(s)));

    [RelayCommand]
    private Task LiftAsync() => owner.LiftAsync(this);
}

/// <summary>Offene Config-Anträge (Vier-Augen-Prinzip) mit Diff freigeben oder ablehnen.</summary>
public sealed partial class ServerApprovalsViewModel(AppServices services, string slug, Action<string, InfoBarSeverity> show)
    : ServerTabViewModel(services, slug, show)
{
    private readonly ServerModerationService _moderation = new(services.Api.Client, services.Api.Http);
    private bool _loaded;

    public ObservableCollection<ChangeItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public async Task EnsureLoadedAsync()
    {
        if (!_loaded)
        {
            _loaded = true;
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private Task ReloadAsync() => RunAsync(async () =>
    {
        var changes = await _moderation.PendingChangesAsync(Slug);
        Items.Clear();
        foreach (var c in changes)
        {
            Items.Add(new ChangeItemViewModel(c, this, Services.Localizer));
        }

        IsEmpty = Items.Count == 0;
    });

    internal async Task DecideAsync(ChangeItemViewModel item, bool approve)
    {
        if (!await Confirm(L(approve ? StringKeys.ApprovalApproveTitle : StringKeys.ApprovalRejectTitle), item.Title))
        {
            return;
        }

        var ok = await RunAsync(() => approve
            ? _moderation.ApproveAsync(Slug, item.Id, item.Note)
            : _moderation.RejectAsync(Slug, item.Id, item.Note));
        if (ok)
        {
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
            Show(L(approve ? StringKeys.ApprovalApproved : StringKeys.ApprovalRejected), InfoBarSeverity.Success);
        }
    }
}

public sealed partial class ChangeItemViewModel(ConfigChange change, ServerApprovalsViewModel owner, ILocalizer localizer) : ObservableObject
{
    public string Id => change.Id;

    public string Title { get; } = change.Path;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        change.Author,
        change.CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        change.Note,
        change.After,
        change.Outdated ? localizer.GetString(StringKeys.ApprovalOutdated) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string Diff => change.Diff;

    /// <summary>Begründung zur Entscheidung (optional).</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [RelayCommand]
    private Task ApproveAsync() => owner.DecideAsync(this, approve: true);

    [RelayCommand]
    private Task RejectAsync() => owner.DecideAsync(this, approve: false);
}
