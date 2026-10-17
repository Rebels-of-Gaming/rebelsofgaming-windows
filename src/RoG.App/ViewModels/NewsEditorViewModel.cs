using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.News;
using RoG.Core.Features.Uploads;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// News-Redaktion (eigenes Fenster): Entwürfe und geplante News, Texte je Sprache (DE Pflicht), Kategorie,
/// Sichtbarkeit, Status (veröffentlichen/planen nur mit <c>news.publish</c>), Titelbild, löschen.
/// </summary>
public sealed partial class NewsEditorViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly NewsAdminService _admin;
    private readonly UploadService _uploads;
    private readonly Dictionary<string, string> _title = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _teaser = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _body = new(StringComparer.Ordinal);
    private IReadOnlyList<NewsCategory> _categories = [];
    private string _language = "de";
    private bool _switching;

    public NewsEditorViewModel(AppServices services, NewsRights rights)
    {
        _services = services;
        Rights = rights;
        _admin = new NewsAdminService(services.Api.Http);
        _uploads = new UploadService(services.Api.Client, services.StorageHttp);
        LanguageOptions = NewsAdminService.Languages.Select(l => new SettingsOption(l, L("NewsEdit_Lang_" + l))).ToList();
        VisibilityOptions = [new("public", L(StringKeys.NewsEditPublic)), new("members", L(StringKeys.NewsEditMembers))];
        StatusOptions = rights.Publish
            ? [new("draft", L(StringKeys.NewsEditDraft)), new("published", L(StringKeys.NewsEditPublished)), new("scheduled", L(StringKeys.NewsEditScheduled))]
            : [new("draft", L(StringKeys.NewsEditDraft))];
        SelectedLanguage = LanguageOptions[0];
        NewDraft();
    }

    public NewsRights Rights { get; }

    /// <summary>Etwas wurde gespeichert/gelöscht – die News-Seite lädt nach dem Schließen neu.</summary>
    public bool Changed { get; private set; }

    public IReadOnlyList<SettingsOption> LanguageOptions { get; }

    public IReadOnlyList<SettingsOption> VisibilityOptions { get; }

    public IReadOnlyList<SettingsOption> StatusOptions { get; }

    public ObservableCollection<SettingsOption> CategoryOptions { get; } = [];

    public ObservableCollection<NewsDraftItem> Drafts { get; } = [];

    [ObservableProperty]
    public partial NewsDraftItem? SelectedDraft { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaved), nameof(CanDelete), nameof(Heading))]
    public partial string? Id { get; set; }

    public bool IsSaved => Id is not null;

    public bool CanDelete => Id is not null && (Rights.Manage || SelectedStatus?.Value == "draft");

    public string Heading => L(Id is null ? StringKeys.NewsEditNew : StringKeys.NewsEditEdit);

    [ObservableProperty]
    public partial SettingsOption? SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Teaser { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SettingsOption? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedVisibility { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScheduled), nameof(CanDelete))]
    public partial SettingsOption? SelectedStatus { get; set; }

    public bool IsScheduled => SelectedStatus?.Value == "scheduled";

    [ObservableProperty]
    public partial DateTimeOffset? PublishDate { get; set; }

    [ObservableProperty]
    public partial TimeSpan PublishTime { get; set; } = new(18, 0, 0);

    [ObservableProperty]
    public partial bool? Pinned { get; set; } = false;

    [ObservableProperty]
    public partial bool? CommentsEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool? PostToDiscord { get; set; } = true;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    /// <summary>Rückfrage (Titel, Text) – setzt das Fenster.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);

    private string L(string key) => _services.Localizer.GetString(key);

    public async Task StartAsync(string? slug)
    {
        await RunAsync(async () =>
        {
            _categories = await _admin.CategoriesAsync();
            CategoryOptions.Clear();
            CategoryOptions.Add(new SettingsOption(string.Empty, L(StringKeys.NewsEditNoCategory)));
            foreach (var c in _categories)
            {
                CategoryOptions.Add(new SettingsOption(c.Id, c.Name));
            }

            SelectedCategory = CategoryOptions[0];
        });
        await LoadDraftsAsync();
        if (slug is not null)
        {
            await OpenAsync(slug);
        }
    }

    private async Task LoadDraftsAsync() => await RunAsync(async () =>
    {
        var drafts = await _admin.ListAsync("draft");
        var scheduled = Rights.Publish ? await _admin.ListAsync("scheduled") : [];
        Drafts.Clear();
        foreach (var n in scheduled.Concat(drafts))
        {
            var when = n.PublishedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            Drafts.Add(new NewsDraftItem(n.Slug, n.Title, L(n.Status == "scheduled" ? StringKeys.NewsEditScheduled : StringKeys.NewsEditDraft) + (when is null ? string.Empty : " · " + when)));
        }
    });

    partial void OnSelectedDraftChanged(NewsDraftItem? value)
    {
        if (value is not null)
        {
            _ = OpenAsync(value.Slug);
        }
    }

    private async Task OpenAsync(string slug)
    {
        IsBusy = true;
        try
        {
            await RunAsync(async () => Fill(await _admin.GetAsync(slug, _categories)));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Fill(NewsDraft draft)
    {
        Id = draft.Id;
        Copy(draft.Title, _title);
        Copy(draft.Teaser, _teaser);
        Copy(draft.Body, _body);
        SelectedCategory = CategoryOptions.FirstOrDefault(c => c.Value == (draft.CategoryId ?? string.Empty)) ?? CategoryOptions.FirstOrDefault();
        SelectedVisibility = VisibilityOptions.FirstOrDefault(v => v.Value == draft.Visibility) ?? VisibilityOptions[0];
        SelectedStatus = StatusOptions.FirstOrDefault(s => s.Value == draft.Status) ?? StatusOptions[0];
        PublishDate = draft.PublishedAt?.ToLocalTime().Date is { } d ? new DateTimeOffset(d) : null;
        PublishTime = draft.PublishedAt?.ToLocalTime().TimeOfDay ?? new TimeSpan(18, 0, 0);
        Pinned = draft.Pinned;
        CommentsEnabled = draft.CommentsEnabled;
        PostToDiscord = draft.PostToDiscord;
        ShowLanguage();
    }

    private static void Copy(IReadOnlyDictionary<string, string> from, Dictionary<string, string> to)
    {
        to.Clear();
        foreach (var (k, v) in from)
        {
            to[k] = v;
        }
    }

    [RelayCommand]
    private void NewDraft()
    {
        SelectedDraft = null;
        Fill(new NewsDraft(null, new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>()));
    }

    partial void OnSelectedLanguageChanged(SettingsOption? oldValue, SettingsOption? newValue)
    {
        if (newValue is null || _switching)
        {
            return;
        }

        Store();
        _language = newValue.Value;
        ShowLanguage();
    }

    /// <summary>Felder der gewählten Sprache in die Wörterbücher übernehmen.</summary>
    private void Store()
    {
        _title[_language] = Title;
        _teaser[_language] = Teaser;
        _body[_language] = Body;
    }

    private void ShowLanguage()
    {
        _switching = true;
        Title = _title.GetValueOrDefault(_language, string.Empty);
        Teaser = _teaser.GetValueOrDefault(_language, string.Empty);
        Body = _body.GetValueOrDefault(_language, string.Empty);
        _switching = false;
    }

    private NewsDraft Current()
    {
        Store();
        DateTimeOffset? at = PublishDate is { } date
            ? new DateTimeOffset(date.Date + PublishTime, TimeZoneInfo.Local.GetUtcOffset(date.Date + PublishTime))
            : null;
        return new NewsDraft(
            Id,
            new Dictionary<string, string>(_title),
            new Dictionary<string, string>(_teaser),
            new Dictionary<string, string>(_body),
            string.IsNullOrEmpty(SelectedCategory?.Value) ? null : SelectedCategory!.Value,
            SelectedVisibility?.Value ?? "public",
            SelectedStatus?.Value ?? "draft",
            at,
            Pinned == true,
            CommentsEnabled != false,
            PostToDiscord != false);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var draft = Current();
        var errors = NewsAdminService.Validate(draft, DateTimeOffset.Now);
        if (errors.Count > 0)
        {
            Show(string.Join(" ", errors.Select(e => L("NewsEdit_Error_" + e))), InfoBarSeverity.Warning);
            return;
        }

        NewsSaved? saved = null;
        if (await RunAsync(async () => saved = await _admin.SaveAsync(draft)))
        {
            Changed = true;
            Id = saved!.Id.Length > 0 ? saved.Id : Id;
            var text = L(draft.Status switch { "published" => StringKeys.NewsEditPublishedDone, "scheduled" => StringKeys.NewsEditScheduledDone, _ => StringKeys.NewsEditSaved });
            Show(saved.Warnings.Count > 0 ? text + " " + string.Join(" ", saved.Warnings) : text, saved.Warnings.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
            await LoadDraftsAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Id is not { } id || !await Confirm(L(StringKeys.NewsEditDeleteTitle), Title))
        {
            return;
        }

        if (await RunAsync(() => _admin.DeleteAsync(id)))
        {
            Changed = true;
            Show(L(StringKeys.NewsEditDeleted), InfoBarSeverity.Success);
            NewDraft();
            await LoadDraftsAsync();
        }
    }

    /// <summary>Titelbild: Bild öffentlich hochladen, dann an der News setzen (erst nach dem ersten Speichern).</summary>
    public async Task SetCoverAsync(Stream content, string fileName, long size)
    {
        if (Id is not { } id)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (await RunAsync(async () =>
            {
                var upload = await _uploads.UploadAsync(content, fileName, size, UploadVisibility.Public);
                await _admin.SetCoverAsync(id, upload.Id);
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

    [RelayCommand]
    private async Task RemoveCoverAsync()
    {
        if (Id is { } id && await RunAsync(() => _admin.RemoveCoverAsync(id)))
        {
            Changed = true;
            Show(L(StringKeys.SquadAdminSaved), InfoBarSeverity.Success);
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

public sealed record NewsDraftItem(string Slug, string Title, string Meta);
