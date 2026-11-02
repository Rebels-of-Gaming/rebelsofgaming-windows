using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Minecraft;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Verwaltung → Kopf-Katalog (Website v0.48.0): Köpfe suchen und nach Kategorie filtern, Namen DE/EN/FR, Kategorie,
/// Tags und Preis bearbeiten, löschen und per Textur-Hash anlegen. Vorschaubilder erzeugt die Website (eigenes CDN).
/// Sichtbar mit Scope <c>minecraft:manage</c> und Recht <c>minecraft.manage</c>.
/// </summary>
public sealed partial class HeadsAdminViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<string, InfoBarSeverity> _show;
    private readonly MinecraftService _minecraft;
    private string? _cursor;
    private bool _loaded;

    public HeadsAdminViewModel(AppServices services, Action<string, InfoBarSeverity> show)
    {
        _services = services;
        _show = show;
        _minecraft = new MinecraftService(services.Api.Http);
        var me = services.CurrentUser;
        CanView = me is not null && me.HasScope(MinecraftService.ManageScope) && me.Can(MinecraftService.ManagePermission);
        CategoryFilterOptions = [new SettingsOption(string.Empty, L(StringKeys.HeadsAllCategories)), .. CategoryOptions];
        SelectedCategoryFilter = CategoryFilterOptions[0];
        EditCategory = CategoryOptions[^2]; // misc
    }

    public bool CanView { get; }

    public IReadOnlyList<SettingsOption> CategoryOptions { get; } =
        MinecraftService.HeadCategories.Select(c => new SettingsOption(c, App.Services.Localizer.GetString("Heads_Category_" + c))).ToList();

    public IReadOnlyList<SettingsOption> CategoryFilterOptions { get; }

    public ObservableCollection<HeadItemViewModel> Items { get; } = [];

    public Func<string, string, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);

    [ObservableProperty]
    public partial SettingsOption SelectedCategoryFilter { get; set; }

    partial void OnSelectedCategoryFilterChanged(SettingsOption value)
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

    // --- Formular (Auswahl = bearbeiten, keine Auswahl = neu) -------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew), nameof(IsExisting), nameof(FormTitle))]
    public partial HeadItemViewModel? Selected { get; set; }

    public bool IsNew => Selected is null;

    public bool IsExisting => Selected is not null;

    public string FormTitle => L(IsNew ? StringKeys.HeadsNewTitle : StringKeys.HeadsEditTitle);

    [ObservableProperty]
    public partial string EditTexture { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditNameDe { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditNameEn { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditNameFr { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SettingsOption EditCategory { get; set; }

    [ObservableProperty]
    public partial string EditTags { get; set; } = string.Empty;

    /// <summary>Leer = Kategoriepreis.</summary>
    [ObservableProperty]
    public partial string EditPrice { get; set; } = string.Empty;

    partial void OnSelectedChanged(HeadItemViewModel? value)
    {
        var h = value?.Head;
        EditTexture = h?.Texture ?? string.Empty;
        EditNameDe = h?.NameDe ?? string.Empty;
        EditNameEn = h?.NameEn ?? string.Empty;
        EditNameFr = h?.NameFr ?? string.Empty;
        EditCategory = CategoryOptions.FirstOrDefault(c => c.Value == h?.Category) ?? CategoryOptions[^2];
        EditTags = h is null ? string.Empty : string.Join(", ", h.Tags);
        EditPrice = h?.Price?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    }

    public async Task EnsureLoadedAsync()
    {
        if (!_loaded)
        {
            _loaded = true;
            await LoadAsync(reset: true);
        }
    }

    [RelayCommand]
    private Task SearchNowAsync() => LoadAsync(reset: true);

    [RelayCommand]
    private Task MoreAsync() => LoadAsync(reset: false);

    [RelayCommand]
    private void New() => Selected = null;

    private async Task LoadAsync(bool reset)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var category = string.IsNullOrEmpty(SelectedCategoryFilter.Value) ? null : SelectedCategoryFilter.Value;
            var page = await _minecraft.HeadsAsync(category, Search, reset ? null : _cursor);
            if (reset)
            {
                Items.Clear();
            }

            foreach (var head in page.Items)
            {
                Items.Add(new HeadItemViewModel(head, _services.Language, _services.Localizer));
            }

            _cursor = page.NextCursor;
            HasMore = _cursor is not null;
            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex) when (ex is ApiProblemException or HttpRequestException or System.Text.Json.JsonException)
        {
            _show(ex is ApiProblemException p ? _services.Localizer.GetError(p.Problem.Code) : _services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!MinecraftService.IsValidHeadName(EditNameDe)
            || (EditNameEn.Length > 0 && !MinecraftService.IsValidHeadName(EditNameEn))
            || (EditNameFr.Length > 0 && !MinecraftService.IsValidHeadName(EditNameFr)))
        {
            _show(L(StringKeys.HeadsInvalidName), InfoBarSeverity.Error);
            return;
        }

        int? price = null;
        if (EditPrice.Trim().Length > 0)
        {
            if (!int.TryParse(EditPrice.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var p) || p is < 1 or > MinecraftService.HeadPriceMax)
            {
                _show(L(StringKeys.HeadsInvalidPrice), InfoBarSeverity.Error);
                return;
            }

            price = p;
        }

        var tags = MinecraftService.ParseTags(EditTags);
        await RunAsync(async () =>
        {
            if (Selected is { } item)
            {
                var h = item.Head;
                var edit = new MinecraftHeadEdit(
                    NameDe: EditNameDe.Trim() != h.NameDe ? EditNameDe : null,
                    NameEn: EditNameEn.Trim().Length > 0 && EditNameEn.Trim() != h.NameEn ? EditNameEn : null,
                    NameFr: EditNameFr.Trim().Length > 0 && EditNameFr.Trim() != h.NameFr ? EditNameFr : null,
                    ClearEn: EditNameEn.Trim().Length == 0 && h.NameEn is not null,
                    ClearFr: EditNameFr.Trim().Length == 0 && h.NameFr is not null,
                    Category: EditCategory.Value != h.Category ? EditCategory.Value : null,
                    Tags: tags.SequenceEqual(h.Tags) ? null : tags,
                    Price: price != h.Price ? price : null,
                    CategoryPrice: price is null && h.Price is not null);
                if (await _minecraft.UpdateHeadAsync(h.Id, edit) is { } updated)
                {
                    var index = Items.IndexOf(item);
                    var replacement = new HeadItemViewModel(updated, _services.Language, _services.Localizer);
                    Items[index] = replacement;
                    Selected = replacement;
                }

                _show(L(StringKeys.HeadsSaved), InfoBarSeverity.Success);
            }
            else
            {
                if (MinecraftService.NormalizeTexture(EditTexture) is null)
                {
                    _show(L(StringKeys.HeadsInvalidTexture), InfoBarSeverity.Error);
                    return;
                }

                var created = await _minecraft.CreateHeadAsync(EditTexture, EditNameDe, EditNameEn, EditNameFr, EditCategory.Value, tags, price);
                if (created is not null)
                {
                    var vm = new HeadItemViewModel(created, _services.Language, _services.Localizer);
                    Items.Insert(0, vm);
                    IsEmpty = false;
                    Selected = vm;
                }

                _show(L(StringKeys.HeadsCreated), InfoBarSeverity.Success);
            }
        });
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is not { } item || !await Confirm(L(StringKeys.HeadsDeleteTitle), item.Name))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _minecraft.DeleteHeadAsync(item.Head.Id);
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
            Selected = null;
            _show(L(StringKeys.HeadsDeleted), InfoBarSeverity.Success);
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (ApiProblemException ex)
        {
            _show(_services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _show(_services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string L(string key) => _services.Localizer.GetString(key);
}

public sealed class HeadItemViewModel(MinecraftHead head, string language, ILocalizer localizer)
{
    public MinecraftHead Head { get; } = head;

    public string Name => Head.Name(language);

    public ImageSource? Preview { get; } = RemoteImage.From(head.ImageUrl, 64);

    public string Meta => string.Join(" · ", new[]
    {
        localizer.GetString("Heads_Category_" + Head.Category),
        Head.Price is { } p
            ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.HeadsPriceValue), p)
            : localizer.GetString(StringKeys.HeadsCategoryPrice),
        Head.Tags.Count > 0 ? string.Join(", ", Head.Tags) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));
}
