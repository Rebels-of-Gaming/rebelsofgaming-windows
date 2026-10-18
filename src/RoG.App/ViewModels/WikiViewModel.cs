using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Content;
using RoG.Core.Features.Wiki;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Wiki: Bereich wählen, Seitenbaum oder Suche, Seite nativ (HTML der API), Versionen mit Vergleich, Bearbeiten mit
/// Vorschau und Konflikterkennung, Beobachten. Gelesene Seiten werden offline gespeichert und ohne Netz gezeigt.
/// </summary>
public sealed partial class WikiViewModel : LoadableViewModel
{
    private readonly WikiService _wiki;
    private readonly RoG.Core.Features.Uploads.UploadService _uploads;
    private readonly Dictionary<string, WikiNamespace> _namespaceInfo = new(StringComparer.Ordinal);
    private bool _namespacesLoaded;
    private string? _baseRevisionId;

    public WikiViewModel(AppServices services)
        : base(services)
    {
        _wiki = new WikiService(services.Api.Client, services.Api.Http, services.Environment.WikiBaseUrl);
        _uploads = new RoG.Core.Features.Uploads.UploadService(services.Api.Client, services.StorageHttp);
    }

    public ObservableCollection<SettingsOption> Namespaces { get; } = [];

    public ObservableCollection<WikiListItem> Items { get; } = [];

    public ObservableCollection<WikiRevisionItemViewModel> Revisions { get; } = [];

    public ObservableCollection<WikiTocItem> Toc { get; } = [];

    /// <summary>Liste zeigt Kategorien statt Seitenbaum.</summary>
    [ObservableProperty]
    public partial bool ShowCategories { get; set; }

    /// <summary>Gewählte Kategorie (Liste zeigt ihre Seiten), sonst <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCategory))]
    public partial string? CategoryTitle { get; set; }

    public bool HasCategory => CategoryTitle is not null;

    public bool HasToc => Toc.Count > 1;

    /// <summary>Neue Seite anlegen (gleiche Rechte wie Bearbeiten).</summary>
    public bool CanCreate => CanEdit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInsertImage))]
    public partial bool IsUploadingImage { get; set; }

    public bool CanInsertImage => !IsUploadingImage;

    [ObservableProperty]
    public partial SettingsOption? SelectedNamespace { get; set; }

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial WikiListItem? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint), nameof(HasPage), nameof(CanReportPage))]
    public partial WikiArticle? Article { get; set; }

    public bool IsDetailOpen => Selected is not null || Article is not null;

    public bool ShowSelectHint => !IsDetailOpen;

    public bool HasPage => Article is not null;

    /// <summary>Melden nur fremder Fassungen (<c>last_editor</c> der API, v0.30.1).</summary>
    public bool CanReportPage => CanWatch && Article is { } page && !page.IsLastEditedBy(Services.CurrentUser?.Id);

    internal string? MyId => Services.CurrentUser?.Id;

    [ObservableProperty]
    public partial RichDocument? Document { get; set; }

    [ObservableProperty]
    public partial string PageMeta { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOfflineCopy { get; set; }

    [ObservableProperty]
    public partial bool IsPageLoading { get; set; }

    [ObservableProperty]
    public partial string WatchLabel { get; set; } = string.Empty;

    /// <summary>Bearbeiten nur mit Recht <c>wiki.edit</c> (irgendwo, Squad-Bereiche) und Scope <c>wiki:write</c>; der Server prüft.</summary>
    public bool CanEdit => Services.CurrentUser is { } me && me.CanAnywhere("wiki.edit") && me.HasScope("wiki:write");

    public bool CanWatch => Services.CurrentUser is not null;

    /// <summary>Vorschläge freigeben/ablehnen (Prüfpflicht) – Recht <c>wiki.approve</c> (global oder im Squad des Bereichs).</summary>
    public bool CanApprove => Services.CurrentUser is { } me && me.CanAnywhere("wiki.approve") && me.HasScope("wiki:write");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReader))]
    public partial bool IsEditing { get; set; }

    public bool ShowReader => !IsEditing;

    [ObservableProperty]
    public partial string EditContent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial RichDocument? PreviewDocument { get; set; }

    [ObservableProperty]
    public partial bool HasPreview { get; set; }

    [ObservableProperty]
    public partial string DiffText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasDiff { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_namespacesLoaded)
            {
                var namespaces = await _wiki.NamespacesAsync(cancellationToken);
                Namespaces.Clear();
                foreach (var n in namespaces)
                {
                    Namespaces.Add(new SettingsOption(n.Slug, n.Name));
                    _namespaceInfo[n.Slug] = n;
                }

                _namespacesLoaded = true;
                _suppressNamespace = true;
                SelectedNamespace = Namespaces.FirstOrDefault();
                _suppressNamespace = false;
            }

            Items.Clear();
            if (_categorySlug is { } category)
            {
                foreach (var page in await _wiki.CategoryPagesAsync(category, cancellationToken))
                {
                    Items.Add(new WikiListItem(page.Id, page.Title, null, 0));
                }
            }
            else if (ShowCategories)
            {
                foreach (var (slug, name, count) in await _wiki.CategoriesAsync(cancellationToken))
                {
                    Items.Add(new WikiListItem(slug, name, count.ToString(CultureInfo.CurrentCulture), 0, IsCategory: true));
                }
            }
            else if (!string.IsNullOrWhiteSpace(SearchText))
            {
                foreach (var hit in await _wiki.SearchAsync(SearchText, cancellationToken))
                {
                    Items.Add(new WikiListItem(hit.Id, hit.Title, StripMarks(hit.Snippet), 0));
                }
            }
            else if (SelectedNamespace is { } ns)
            {
                foreach (var entry in await _wiki.TreeAsync(ns.Value, cancellationToken))
                {
                    Items.Add(new WikiListItem(entry.Id, entry.Title, null, entry.Depth));
                }
            }
        }
        catch (ApiProblemException ex) when (ex.Problem.IsNetworkError)
        {
            // Ohne Netz: offline gespeicherte Seiten zeigen
            Items.Clear();
            foreach (var page in await Services.WikiOffline.AllAsync(Services.Language, cancellationToken))
            {
                Items.Add(new WikiListItem(page.Id, page.Title, page.NamespaceName, 0));
            }

            if (Items.Count == 0)
            {
                throw;
            }

            Show(Services.Localizer.GetString(StringKeys.WikiOfflineList), InfoBarSeverity.Warning);
        }
    }

    private bool _suppressNamespace;
    private string? _categorySlug;

    partial void OnShowCategoriesChanged(bool value)
    {
        _categorySlug = null;
        CategoryTitle = null;
        if (_namespacesLoaded)
        {
            _ = LoadAsync();
        }
    }

    [RelayCommand]
    private Task LeaveCategoryAsync()
    {
        _categorySlug = null;
        CategoryTitle = null;
        return LoadAsync();
    }

    partial void OnSelectedNamespaceChanged(SettingsOption? value)
    {
        if (!_suppressNamespace && _namespacesLoaded)
        {
            SearchText = null;
            _ = LoadAsync();
        }
    }

    partial void OnSelectedChanged(WikiListItem? value)
    {
        if (value is { IsCategory: true })
        {
            _categorySlug = value.Id;
            CategoryTitle = value.Title;
            Selected = null;
            _ = LoadAsync();
        }
        else if (value is not null)
        {
            _ = OpenPageAsync(value.Id);
        }
    }

    [RelayCommand]
    private Task SearchAsync() => LoadAsync();

    /// <summary>Deep-Link (<c>bereich/…/seite</c>) aus <c>rog://wiki/…</c> oder einem Link im Inhalt.</summary>
    public async Task OpenPathAsync(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        await LoadAsync();
        if (segments.Length < 2)
        {
            if (segments.Length == 1 && Namespaces.FirstOrDefault(n => n.Value == segments[0]) is { } ns)
            {
                SelectedNamespace = ns;
            }

            return;
        }

        await LoadPageAsync(() => _wiki.PageBySlugAsync(segments[0], segments[^1], Services.Language), null);
    }

    private Task OpenPageAsync(string id) =>
        LoadPageAsync(() => _wiki.PageAsync(id, Services.Language), id);

    private async Task LoadPageAsync(Func<Task<WikiArticle>> load, string? offlineId)
    {
        IsEditing = false;
        IsPageLoading = true;
        HasDiff = false;
        Revisions.Clear();
        try
        {
            var page = await load();
            ShowPage(page, offline: false);
            try
            {
                await Services.WikiOffline.SaveAsync(page);
            }
            catch (IOException)
            {
                // Offline-Kopie ist Beiwerk
            }
        }
        catch (ApiProblemException ex)
        {
            // Ohne Netz: Offline-Kopie zeigen, falls vorhanden
            if (ex.Problem.IsNetworkError && offlineId is not null
                && await Services.WikiOffline.LoadAsync(offlineId, Services.Language) is { } cached)
            {
                ShowPage(cached, offline: true);
            }
            else
            {
                Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
            }
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
        finally
        {
            IsPageLoading = false;
        }
    }

    private void ShowPage(WikiArticle page, bool offline)
    {
        Article = page;
        IsOfflineCopy = offline;
        Document = new HtmlContentParser(ContentPolicy.Platform, page.WebUrl ?? Services.Environment.WikiBaseUrl).Parse(page.Html);
        PageMeta = string.Join(" · ", new[]
        {
            page.NamespaceName,
            page.UpdatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            page.Categories.Count > 0 ? string.Join(", ", page.Categories) : null,
        }.Where(s => !string.IsNullOrEmpty(s)));
        WatchLabel = Services.Localizer.GetString(page.Watching == true ? StringKeys.WikiUnwatch : StringKeys.WikiWatch);
        Toc.Clear();
        foreach (var entry in page.Toc)
        {
            Toc.Add(new WikiTocItem(entry.Text, new Thickness(Math.Max(0, entry.Level - 2) * 16, 0, 0, 0)));
        }

        OnPropertyChanged(nameof(HasToc));
    }

    /// <summary>Neue Seite im gewählten Bereich anlegen und öffnen; <c>false</c> bei Fehler (Meldung in der InfoBar).</summary>
    public async Task<bool> CreatePageAsync(string title, string format, string? parentId, string content, string? summary)
    {
        if (SelectedNamespace is not { } ns)
        {
            return false;
        }

        WikiCreated? created = null;
        if (!await RunAsync(async () => created = await _wiki.CreatePageAsync(ns.Value, Services.Language, title, content, format, summary, parentId)))
        {
            return false;
        }

        Show(Services.Localizer.GetString(created!.Status == "pending" ? StringKeys.WikiPending : StringKeys.WikiCreated), InfoBarSeverity.Success);
        await LoadAsync();
        await OpenPageAsync(created.PageId);
        return true;
    }

    /// <summary>Seiten des aktuellen Baums als mögliche Elternseiten (Titel eingerückt).</summary>
    public IReadOnlyList<SettingsOption> ParentOptions() =>
        [new(string.Empty, Services.Localizer.GetString(StringKeys.WikiNoParent)),
         .. Items.Where(i => !i.IsCategory).Select(i => new SettingsOption(i.Id, new string(' ', i.Depth * 2) + i.Title))];

    /// <summary>
    /// Bild hochladen (Sichtbarkeit passend zum Bereich), im Wiki registrieren und am Ende des Editors einbinden.
    /// </summary>
    public async Task InsertImageAsync(Stream content, string fileName, long size)
    {
        if (Article is not { } page || IsUploadingImage)
        {
            return;
        }

        if (RoG.Core.Features.Uploads.UploadService.MimeFor(fileName) is not { } mime || !mime.StartsWith("image/", StringComparison.Ordinal))
        {
            Show(Services.Localizer.GetString(StringKeys.WikiImageOnly), InfoBarSeverity.Warning);
            return;
        }

        IsUploadingImage = true;
        try
        {
            var visibility = WikiService.UploadVisibilityFor(_namespaceInfo.TryGetValue(page.NamespaceSlug, out var ns) ? ns.UploadVisibility : null);
            await RunAsync(async () =>
            {
                var upload = await _uploads.UploadAsync(content, fileName, size, visibility);
                var file = await _wiki.RegisterFileAsync(upload.Id, page.NamespaceSlug, fileName, null);
                var embed = WikiService.FileEmbed(page.Format, file.Name.Length > 0 ? file.Name : fileName, upload.Links.Direct ?? file.Source);
                EditContent = EditContent.TrimEnd() + "\n\n" + embed + "\n";
                Show(Services.Localizer.GetString(StringKeys.WikiImageInserted), InfoBarSeverity.Success);
            });
        }
        finally
        {
            IsUploadingImage = false;
        }
    }

    [RelayCommand]
    private async Task ToggleWatchAsync()
    {
        if (Article is not { } page)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var watching = await _wiki.ToggleWatchAsync(page.Id);
            Article = page with { Watching = watching };
            WatchLabel = Services.Localizer.GetString(watching ? StringKeys.WikiUnwatch : StringKeys.WikiWatch);
            Show(Services.Localizer.GetString(watching ? StringKeys.WikiWatching : StringKeys.WikiNotWatching), InfoBarSeverity.Success);
        });
    }

    /// <summary>Angezeigte Seite melden – mit der Sprache der Fassung, die gerade zu sehen ist.</summary>
    [RelayCommand]
    private Task ReportAsync() => Article is { } page ? Controls.ReportDialog.ShowAsync("wiki_page", page.Id, page.Locale) : Task.CompletedTask;

    [RelayCommand]
    private async Task OpenInBrowserAsync()
    {
        if (Article?.WebUrl is { } url)
        {
            await Services.Browser.OpenAsync(url);
        }
    }

    [RelayCommand]
    private async Task LoadRevisionsAsync()
    {
        if (Article is not { } page)
        {
            return;
        }

        await RunAsync(async () =>
        {
            Revisions.Clear();
            foreach (var r in await _wiki.RevisionsAsync(page.Id))
            {
                Revisions.Add(new WikiRevisionItemViewModel(r, this));
            }
        });
    }

    internal Task CompareAsync(WikiRevisionItemViewModel revision) => RunAsync(async () =>
    {
        var old = await _wiki.RevisionContentAsync(revision.Id);
        var current = Article?.Content ?? (Article?.RevisionId is { } id ? await _wiki.RevisionContentAsync(id) : string.Empty);
        DiffText = string.Join("\n", LineDiff.Compare(old, current).Where(l => l.Kind != ' ').Select(l => $"{l.Kind} {l.Text}"));
        if (DiffText.Length == 0)
        {
            DiffText = Services.Localizer.GetString(StringKeys.ConfigUnchanged);
        }

        HasDiff = true;
    });

    internal async Task DecideAsync(WikiRevisionItemViewModel revision, bool approve)
    {
        if (Article is { } page && await RunAsync(() => approve ? _wiki.ApproveAsync(revision.Id) : _wiki.RejectAsync(revision.Id)))
        {
            Show(Services.Localizer.GetString(approve ? StringKeys.WikiApproved : StringKeys.WikiRejected), InfoBarSeverity.Success);
            await OpenPageAsync(page.Id);
            await LoadRevisionsAsync();
        }
    }

    internal async Task RestoreAsync(WikiRevisionItemViewModel revision)
    {
        if (Article is { } page && await RunAsync(() => _wiki.RestoreAsync(revision.Id)))
        {
            Show(Services.Localizer.GetString(StringKeys.WikiRestored), InfoBarSeverity.Success);
            await OpenPageAsync(page.Id);
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Article is not { } page)
        {
            return;
        }

        // Quelltext: kommt mit der Seite; sonst aus der aktuellen Version
        var content = page.Content;
        if (content is null && page.RevisionId is { } revision)
        {
            await RunAsync(async () => content = await _wiki.RevisionContentAsync(revision));
        }

        EditContent = content ?? string.Empty;
        EditSummary = string.Empty;
        _baseRevisionId = page.RevisionId;
        HasPreview = false;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private Task PreviewAsync() => Article is not { } page ? Task.CompletedTask : RunAsync(async () =>
    {
        var html = await _wiki.RenderAsync(page.Id, EditContent, page.Format, page.Title);
        PreviewDocument = new HtmlContentParser(ContentPolicy.Platform, page.WebUrl ?? Services.Environment.WikiBaseUrl).Parse(html);
        HasPreview = true;
    });

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Article is not { } page)
        {
            return;
        }

        try
        {
            var outcome = await _wiki.SaveAsync(page.Id, page.Locale, page.Title, EditContent, page.Format, EditSummary, _baseRevisionId);
            IsEditing = false;
            Show(Services.Localizer.GetString(outcome == WikiSaveOutcome.Pending ? StringKeys.WikiPending : StringKeys.WikiSaved), InfoBarSeverity.Success);
            await OpenPageAsync(page.Id);
        }
        catch (WikiEditConflictException conflict)
        {
            // Fremde Änderung: Unterschied zeigen, nächster Klick auf Speichern überschreibt bewusst
            _baseRevisionId = conflict.CurrentRevisionId;
            DiffText = string.Join("\n", LineDiff.Compare(conflict.CurrentContent, EditContent).Where(l => l.Kind != ' ').Select(l => $"{l.Kind} {l.Text}"));
            HasDiff = true;
            Show(string.Format(CultureInfo.CurrentCulture, Services.Localizer.GetString(StringKeys.WikiConflict), conflict.UpdatedBy ?? "?"), InfoBarSeverity.Warning);
        }
        catch (ApiProblemException ex)
        {
            Show(ex.Problem.Code == "validation_failed" && ex.Problem.Detail is { } d ? d : Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    public void CloseDetail()
    {
        Selected = null;
        Article = null;
        IsEditing = false;
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
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
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

    private static string StripMarks(string snippet) =>
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(snippet, "<[^>]+>", string.Empty));
}

public sealed record WikiTocItem(string Text, Thickness Indent);

public sealed record WikiListItem(string Id, string Title, string? Subtitle, int Depth, bool IsCategory = false)
{
    public Thickness Indent => new(Depth * 16, 0, 0, 0);

    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);
}

public sealed partial class WikiRevisionItemViewModel(WikiRevision revision, WikiViewModel owner)
{
    public string Id => revision.Id;

    public string Text { get; } = string.Join(" · ", new[]
    {
        revision.CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        revision.Author,
        revision.Summary,
        revision.Status == "approved" ? null : revision.Status,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool CanRestore => owner.CanEdit && revision.Status == "approved";

    public bool CanDecide => owner.CanApprove && revision.Status == "pending";

    public bool CanReport => owner.CanWatch && !revision.IsBy(owner.MyId);

    [RelayCommand]
    private Task ReportAsync() => Controls.ReportDialog.ShowAsync("wiki_revision", revision.Id);

    [RelayCommand]
    private Task CompareAsync() => owner.CompareAsync(this);

    [RelayCommand]
    private Task ApproveAsync() => owner.DecideAsync(this, approve: true);

    [RelayCommand]
    private Task RejectAsync() => owner.DecideAsync(this, approve: false);

    [RelayCommand]
    private Task RestoreAsync() => owner.RestoreAsync(this);
}
