using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Minecraft;
using RoG.Core.Localization;
using RoG.Core.Navigation;

namespace RoG.App.ViewModels;

/// <summary>
/// Verwaltung → Minecraft (Website v0.36.0, Guide 44): alle verknüpften Accounts mit Konto und Server der Verknüpfung,
/// Suche nach Name/UUID, Filter Edition, trennen. Sichtbar mit Scope <c>minecraft:manage</c> und Recht
/// <c>minecraft.manage</c> (nur global); Rangschutz beim Trennen prüft der Server (<c>403 forbidden</c>).
/// </summary>
public sealed partial class MinecraftAdminViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<string, InfoBarSeverity> _show;
    private readonly MinecraftService _minecraft;
    private string? _cursor;
    private bool _loaded;

    public MinecraftAdminViewModel(AppServices services, Action<string, InfoBarSeverity> show)
    {
        _services = services;
        _show = show;
        _minecraft = new MinecraftService(services.Api.Http);
        var me = services.CurrentUser;
        CanView = me is not null && me.HasScope(MinecraftService.ManageScope) && me.Can(MinecraftService.ManagePermission);
        EditionOptions =
        [
            new SettingsOption(string.Empty, L(StringKeys.MinecraftAdminEditionAll)),
            .. MinecraftService.Editions.Select(e => new SettingsOption(e, L("Minecraft_Edition_" + e))),
        ];
        SelectedEdition = EditionOptions[0];
    }

    public bool CanView { get; }

    public ObservableCollection<MinecraftAdminItemViewModel> Items { get; } = [];

    public IReadOnlyList<SettingsOption> EditionOptions { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedEdition { get; set; }

    partial void OnSelectedEditionChanged(SettingsOption value)
    {
        if (_loaded)
        {
            _ = LoadAsync(reset: true);
        }
    }

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    /// <summary>Trennen bestätigen lassen (Titel, Text) – setzt die Seite.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await LoadAsync(reset: true);
    }

    [RelayCommand]
    private Task ReloadAsync() => LoadAsync(reset: true);

    [RelayCommand]
    private Task SearchNowAsync() => LoadAsync(reset: true);

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
            var edition = string.IsNullOrEmpty(SelectedEdition.Value) ? null : SelectedEdition.Value;
            var page = await _minecraft.ListAsync(Search, edition, null, reset ? null : _cursor);
            if (reset)
            {
                Items.Clear();
            }

            foreach (var account in page.Items)
            {
                Items.Add(new MinecraftAdminItemViewModel(account, this, _services.Localizer));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
            IsEmpty = Items.Count == 0;
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

    internal async Task UnlinkAsync(MinecraftAdminItemViewModel item)
    {
        var text = string.Format(CultureInfo.CurrentCulture, L(StringKeys.MinecraftAdminUnlinkText), item.Name, item.UserName ?? "—");
        if (!await Confirm(L(StringKeys.MinecraftUnlinkTitle), text))
        {
            return;
        }

        try
        {
            await _minecraft.AdminUnlinkAsync(item.Id);
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
            _show(string.Format(CultureInfo.CurrentCulture, L(StringKeys.MinecraftUnlinked), item.Name), InfoBarSeverity.Success);
        }
        catch (ApiProblemException ex)
        {
            _show(_services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException)
        {
            _show(_services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    internal void OpenProfile(string slug) => _services.Navigate(new AppRoute(AppSection.Members, slug));

    private string L(string key) => _services.Localizer.GetString(key);
}

public sealed partial class MinecraftAdminItemViewModel(MinecraftAccount account, MinecraftAdminViewModel owner, ILocalizer localizer)
{
    public string Id => account.Id;

    public string Name => account.Name;

    public string? UserName => account.UserName;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        MinecraftAccountsViewModel.EditionText(localizer, account.Edition, account.Platform),
        account.UserName is { } user ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.MinecraftAdminUser), user) : null,
        account.ServerName is { } server ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.MinecraftAdminVia), server) : null,
        account.LinkedAt is { } linked ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.MinecraftLinkedAt), linked.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string Uuid => account.Uuid;

    public bool HasProfile => account.UserSlug is not null;

    [RelayCommand]
    private Task UnlinkAsync() => owner.UnlinkAsync(this);

    [RelayCommand]
    private void OpenProfile()
    {
        if (account.UserSlug is { } slug)
        {
            owner.OpenProfile(slug);
        }
    }
}
