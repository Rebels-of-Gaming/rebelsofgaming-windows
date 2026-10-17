using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Admin;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Allgemeiner Editor für einen Bereich der Website-Verwaltung (<see cref="AdminArea"/>): Liste mit Suche, Formular aus
/// der Felddefinition, Anlegen/Speichern/Löschen. Logik (Lesen, Prüfen, Body) liegt in <see cref="AdminForm"/>.
/// </summary>
public sealed partial class AdminEditorViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly AdminResourceService _resources;
    private readonly List<AdminRecordViewModel> _all = [];
    private string? _cursor;
    private AdminFormValues? _original;
    private AdminRecord? _current;

    public AdminEditorViewModel(AppServices services, AdminArea area)
    {
        _services = services;
        Area = area;
        _resources = new AdminResourceService(services.Api.Http);
        CanWrite = area.IsWritableBy(services.CurrentUser);
        Title = services.Localizer.GetString(area.TitleKey);
        Help = area.HelpKey is null ? null : services.Localizer.GetString(area.HelpKey);
    }

    public AdminArea Area { get; }

    public string Title { get; }

    public string? Help { get; }

    public bool HasHelp => Help is not null;

    public bool CanWrite { get; }

    public bool CanCreate => CanWrite && Area.CanCreate && !Area.IsSingleton;

    /// <summary>Liste links – nicht bei Einstellungen (ein Objekt).</summary>
    public bool ShowList => !Area.IsSingleton;

    public bool HasWebPath => Area.WebPath is not null && _services.Environment.AdminBaseUrl is not null;

    public ObservableCollection<AdminRecordViewModel> Records { get; } = [];

    public ObservableCollection<AdminFieldViewModel> Fields { get; } = [];

    /// <summary>Rückfrage vor dem Löschen – setzt das Fenster.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);

    /// <summary>Etwas wurde gespeichert oder gelöscht.</summary>
    public bool Changed { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial AdminRecordViewModel? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(CanDelete), nameof(HasNoForm))]
    public partial bool HasForm { get; set; }

    public bool HasNoForm => !HasForm && !Area.IsSingleton;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(CanDelete))]
    public partial bool IsNew { get; set; }

    [ObservableProperty]
    public partial string FormTitle { get; set; } = string.Empty;

    public bool CanSave => HasForm && CanWrite && (IsNew ? Area.CanCreate : Area.CanUpdate);

    public bool CanDelete => HasForm && CanWrite && !IsNew && Area.CanDelete;

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    partial void OnSearchChanged(string value) => ApplyFilter();

    partial void OnSelectedChanged(AdminRecordViewModel? value)
    {
        if (value is not null)
        {
            _ = OpenAsync(value.Record);
        }
    }

    public async Task LoadAsync()
    {
        if (Area.IsSingleton)
        {
            IsLoading = true;
            await RunAsync(async () => await ShowFormAsync(await _resources.LoadSingletonAsync(Area, _services.Language)));
            IsLoading = false;
            return;
        }

        _cursor = null;
        _all.Clear();
        await LoadPageAsync();
    }

    /// <summary>Was der Editor nicht kann (Reihenfolge, Bilder, verschachtelte Listen), in der Website-Verwaltung öffnen.</summary>
    [RelayCommand]
    private async Task OpenWebAsync()
    {
        if (Area.WebPath is { } path && _services.Environment.AdminBaseUrl is { } admin)
        {
            await _services.Browser.OpenAsync(new Uri(admin, path));
        }
    }

    [RelayCommand]
    private Task MoreAsync() => LoadPageAsync();

    private async Task LoadPageAsync()
    {
        IsLoading = true;
        await RunAsync(async () =>
        {
            var page = await _resources.ListAsync(Area, _services.Language, _cursor);
            _all.AddRange(page.Items.Select(r => new AdminRecordViewModel(r)));
            _cursor = page.NextCursor;
            HasMore = Area.Paged && page.NextCursor is not null;
            ApplyFilter();
        });
        IsLoading = false;
    }

    private void ApplyFilter()
    {
        var q = Search.Trim();
        Records.Clear();
        foreach (var r in _all.Where(r => q.Length == 0 || r.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase) || (r.Subtitle?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false)))
        {
            Records.Add(r);
        }

        IsEmpty = !IsLoading && Records.Count == 0;
    }

    private async Task OpenAsync(AdminRecord record)
    {
        await RunAsync(async () =>
        {
            var fresh = await _resources.GetAsync(Area, record, _services.Language);
            await ShowFormAsync(fresh);
        });
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        Selected = null;
        await ShowFormAsync(null);
    }

    private async Task ShowFormAsync(AdminRecord? record)
    {
        _current = record;
        _original = AdminForm.Read(Area, record?.Raw, _services.Language);
        IsNew = record is null;
        FormTitle = Area.IsSingleton ? Title : record?.Title ?? _services.Localizer.GetString(StringKeys.AdminFormNew);
        Fields.Clear();
        foreach (var f in Area.Fields)
        {
            var field = new AdminFieldViewModel(f, _original.GetValueOrDefault(f.Key), _services.Localizer, enabled: CanWrite && (IsNew || !f.CreateOnly), _services.Language);
            if (f.Type == AdminFieldType.Reference && f.Reference is { } refKey && AdminAreas.Find(refKey) is { } refArea)
            {
                await field.LoadReferenceAsync(_resources, refArea, _services.Language);
            }

            Fields.Add(field);
        }

        HasForm = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var values = new AdminFormValues();
        foreach (var f in Fields)
        {
            values[f.Field.Key] = f.Value;
        }

        var errors = AdminForm.Validate(Area, values, IsNew);
        foreach (var f in Fields)
        {
            f.Error = errors.TryGetValue(f.Field.Key, out var e) ? _services.Localizer.GetString(e) : null;
        }

        if (errors.Count > 0)
        {
            Show(_services.Localizer.GetString(StringKeys.AdminFormCheckFields), InfoBarSeverity.Warning);
            return;
        }

        var body = AdminForm.BuildBody(Area, IsNew ? null : _original, values);
        if (!IsNew && body.Count == 0)
        {
            Show(_services.Localizer.GetString(StringKeys.AdminFormNoChanges), InfoBarSeverity.Informational);
            return;
        }

        await RunAsync(async () =>
        {
            var saved = await _resources.SaveAsync(Area, IsNew ? null : _current, body, _services.Language);
            Changed = true;
            Show(_services.Localizer.GetString(StringKeys.AdminFormSaved), InfoBarSeverity.Success);
            await LoadAsync();
            if (Area.IsSingleton)
            {
                return;
            }

            var again = saved is null ? null : _all.FirstOrDefault(r => r.Record.Id == saved.Id);
            if (again is not null)
            {
                Selected = again;
            }
            else if (saved is not null)
            {
                await ShowFormAsync(saved);
            }
        });
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_current is not { } record
            || !await Confirm(_services.Localizer.GetString(StringKeys.AdminFormDeleteTitle), string.Format(System.Globalization.CultureInfo.CurrentCulture, _services.Localizer.GetString(StringKeys.AdminFormDeleteText), record.Title)))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _resources.DeleteAsync(Area, record);
            Changed = true;
            HasForm = false;
            Fields.Clear();
            Show(_services.Localizer.GetString(StringKeys.AdminFormDeleted), InfoBarSeverity.Success);
            await LoadAsync();
        });
    }

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiProblemException ex)
        {
            Show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : _services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            Show(_services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }
}

public sealed class AdminRecordViewModel(AdminRecord record)
{
    public AdminRecord Record => record;

    public string Title => record.Title;

    public string? Subtitle => record.Subtitle;

    public bool HasSubtitle => record.Subtitle is not null;
}

/// <summary>Ein Formularfeld; die Vorlage wählt <see cref="Controls.AdminFieldTemplateSelector"/> nach <see cref="AdminField.Type"/>.</summary>
public sealed partial class AdminFieldViewModel : ObservableObject
{
    public AdminFieldViewModel(AdminField field, object? value, ILocalizer localizer, bool enabled, string locale)
    {
        Field = field;
        IsEnabled = enabled;
        Label = localizer.GetString(field.LabelKey) + (field.Required ? " *" : string.Empty);
        Help = field.HelpKey is null ? null : localizer.GetString(field.HelpKey);
        Placeholder = field.Type switch
        {
            AdminFieldType.Color => "#E0324A",
            AdminFieldType.Date => "2026-10-17",
            AdminFieldType.DateTime => "2026-10-17 20:30",
            AdminFieldType.Url => "https://",
            AdminFieldType.Lines => localizer.GetString(StringKeys.AdminFormOnePerLine),
            _ => string.Empty,
        };
        if (field.IsTranslated)
        {
            var map = value as IReadOnlyDictionary<string, string> ?? new Dictionary<string, string>();
            De = map.GetValueOrDefault("de") ?? string.Empty;
            En = map.GetValueOrDefault("en") ?? string.Empty;
            Fr = map.GetValueOrDefault("fr") ?? string.Empty;

            // API lieferte nur eine Sprache: nur diese zeigen, die anderen bleiben auf der Website unverändert
            _partial = value is AdminTranslations { Partial: true };
            ShowDe = !_partial || locale == "de";
            ShowEn = !_partial || locale == "en";
            ShowFr = !_partial || locale == "fr";
            PartialHint = _partial ? localizer.GetString(StringKeys.AdminFormPartialLanguage) : null;
        }
        else if (field.Type == AdminFieldType.Bool)
        {
            IsOn = value is true;
        }
        else
        {
            Text = value as string ?? string.Empty;
        }

        if (field.Choices is { } choices)
        {
            if (!field.Required)
            {
                Choices.Add(new SettingsOption(string.Empty, "–"));
            }

            foreach (var c in choices)
            {
                var label = localizer.GetString(c.LabelKey);
                Choices.Add(new SettingsOption(c.Value, label));
            }

            SelectedChoice = Choices.FirstOrDefault(c => c.Value == Text) ?? (field.Required ? null : Choices[0]);
        }
    }

    private readonly bool _partial;

    public AdminField Field { get; }

    public bool ShowDe { get; } = true;

    public bool ShowEn { get; } = true;

    public bool ShowFr { get; } = true;

    public string? PartialHint { get; }

    public bool HasPartialHint => PartialHint is not null;

    public string Label { get; }

    public string? Help { get; }

    public bool HasHelp => Help is not null;

    public string Placeholder { get; }

    public bool IsEnabled { get; }

    public bool AcceptsReturn => Field.Type is AdminFieldType.Multiline or AdminFieldType.TranslatedMultiline or AdminFieldType.Lines;

    public double MinTextHeight => AcceptsReturn ? 96 : 0;

    public int MaxLength => Field.MaxLength ?? 0;

    public ObservableCollection<SettingsOption> Choices { get; } = [];

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    [ObservableProperty]
    public partial string De { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string En { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Fr { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SettingsOption? SelectedChoice { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    partial void OnSelectedChoiceChanged(SettingsOption? value) => Text = value?.Value ?? string.Empty;

    /// <summary>Wert für <see cref="AdminForm"/>.</summary>
    public object? Value => Field.IsTranslated ? Translations() : Field.Type == AdminFieldType.Bool ? IsOn : Text;

    private AdminTranslations Translations()
    {
        var map = new AdminTranslations { Partial = _partial };
        if (ShowDe)
        {
            map["de"] = De;
        }

        if (ShowEn)
        {
            map["en"] = En;
        }

        if (ShowFr)
        {
            map["fr"] = Fr;
        }

        return map;
    }

    /// <summary>Auswahl für Verweise aus der Liste des anderen Bereichs.</summary>
    public async Task LoadReferenceAsync(AdminResourceService resources, AdminArea area, string locale)
    {
        try
        {
            var current = Text;
            var page = await resources.ListAsync(area, locale);
            if (!Field.Required)
            {
                Choices.Add(new SettingsOption(string.Empty, "–"));
            }

            foreach (var r in page.Items)
            {
                Choices.Add(new SettingsOption(AdminForm.Display(r.Raw, "id", locale) ?? r.Id, r.Title));
            }

            // Wert, den die Liste nicht kennt (z. B. Slug statt ID): als eigene Wahl behalten statt still zu leeren
            if (current.Length > 0 && Choices.All(c => c.Value != current))
            {
                Choices.Add(new SettingsOption(current, current));
            }

            SelectedChoice = Choices.FirstOrDefault(c => c.Value == current);
        }
        catch (Exception ex) when (ex is ApiProblemException or HttpRequestException or System.Text.Json.JsonException)
        {
            // ohne Liste bleibt das Feld leer – der Server meldet fehlende Pflichtwerte
        }
    }
}
