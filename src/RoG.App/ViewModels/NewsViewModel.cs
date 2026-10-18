using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RoG.App.Services;
using RoG.Core.Content;
using RoG.Core.Features.News;
using RoG.Core.Features.Social;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>News: Liste (Cursor, „Mehr laden“) und Artikel daneben bzw. darüber.</summary>
public sealed partial class NewsViewModel : LoadableViewModel
{
    private readonly NewsService _news;
    private string? _cursor;

    public NewsViewModel(AppServices services)
        : base(services)
    {
        _news = new NewsService(services.Api.Client, services.Environment.WebBaseUrl);
        Detail = new NewsDetailViewModel(services, _news);
    }

    public ObservableCollection<NewsItemViewModel> Items { get; } = [];

    public NewsDetailViewModel Detail { get; }

    /// <summary>Redaktion: Rechte aus <c>/v1/me</c> und Token-Scope <c>news:write</c>.</summary>
    public NewsRights Rights => NewsRights.For(Services.CurrentUser);

    public bool CanWrite => Rights.Write;

    /// <summary>Veröffentlichte News bearbeiten darf nur, wer veröffentlichen darf (<c>news.publish</c>).</summary>
    public bool CanEditAll => Rights.Publish;

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial NewsItemViewModel? Selected { get; set; }

    /// <summary>Detail per Link geöffnet, ohne dass der Eintrag in der geladenen Liste steht.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial bool DetailForced { get; set; }

    public bool IsDetailOpen => Selected is not null || DetailForced;

    public bool ShowSelectHint => !IsDetailOpen;

    /// <summary>Liste laden und den Eintrag <paramref name="slug"/> öffnen (Suche, Links, später Deep Links).</summary>
    public async Task OpenAsync(string slug)
    {
        await LoadAsync();
        Selected = Items.FirstOrDefault(i => i.Slug == slug);
        if (Selected is null)
        {
            DetailForced = true;
            await Detail.ShowAsync(slug);
        }
    }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var page = await _news.ListAsync(null, cancellationToken);
        var selectedSlug = Selected?.Slug;
        Items.Clear();
        foreach (var item in page.Items)
        {
            Items.Add(new NewsItemViewModel(item));
        }

        _cursor = page.NextCursor;
        HasMore = page.HasMore;
        if (selectedSlug is not null)
        {
            Selected = Items.FirstOrDefault(i => i.Slug == selectedSlug);
        }

        _ = LoadSocialStatesAsync();
    }

    /// <summary>Social-Media-Symbole für die Redaktion – eine Abfrage für die ganze Liste, Fehler blenden sie nur aus.</summary>
    private async Task LoadSocialStatesAsync()
    {
        if (Services.CurrentUser is not { } me || !me.Can(SocialSourceService.Permission) || !me.HasScope(SocialSourceService.ReadScope))
        {
            return;
        }

        try
        {
            _socialStates = await new SocialSourceService(Services.Api.Http).ListStatesAsync(SocialSources.News);
            ApplySocial(Items);
        }
        catch (RoG.Core.Api.ApiProblemException)
        {
            // Symbole sind Beiwerk
        }
    }

    private IReadOnlyDictionary<string, string>? _socialStates;

    private void ApplySocial(IEnumerable<NewsItemViewModel> items)
    {
        if (_socialStates is null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (_socialStates.TryGetValue(item.Id, out var state))
            {
                item.SetSocial(state, Services.Localizer);
            }
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_cursor is null || IsLoadingMore)
        {
            return;
        }

        IsLoadingMore = true;
        try
        {
            var page = await _news.ListAsync(_cursor);
            var added = page.Items.Select(i => new NewsItemViewModel(i)).ToList();
            foreach (var item in added)
            {
                Items.Add(item);
            }

            ApplySocial(added);

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
        }
        catch (Exception ex) when (ex is RoG.Core.Api.ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // „Mehr laden“ bleibt sichtbar – erneut versuchen
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    partial void OnSelectedChanged(NewsItemViewModel? value)
    {
        if (value is not null)
        {
            DetailForced = false;
        }

        if (value is not null)
        {
            _ = Detail.ShowAsync(value.Slug);
        }
    }
}

/// <summary>Eine News in der Liste.</summary>
public sealed partial class NewsItemViewModel(NewsSummary news) : ObservableObject
{
    public string Id => news.Id;

    public string Slug => news.Slug;

    /// <summary>Social-Media-Symbol (nur für <c>social.publish</c>, nachgeladen).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSocial))]
    public partial string SocialGlyph { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Media.Brush? SocialBrush { get; set; }

    [ObservableProperty]
    public partial string SocialText { get; set; } = string.Empty;

    public bool HasSocial => SocialGlyph.Length > 0;

    internal void SetSocial(string state, ILocalizer localizer)
    {
        var badge = SocialStates.Badge(state);
        (SocialGlyph, var brush) = badge switch
        {
            SocialBadge.Posted => ("\uE73E", "RoGGreenBrush"),
            SocialBadge.Partial => ("\uE7BA", "RoGGoldBrush"),
            SocialBadge.Failed => ("\uE783", "RoGRedBrush"),
            SocialBadge.Pending => ("\uE823", "RoGMutedBrush"),
            SocialBadge.Draft => ("\uE70F", "RoGMutedBrush"),
            _ => (string.Empty, "RoGMutedBrush"),
        };
        SocialBrush = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources[brush];
        SocialText = badge == SocialBadge.None ? string.Empty : localizer.GetString("Social_State_" + state);
    }

    public string Title => news.Title;

    public string Teaser => news.Teaser;

    public bool Pinned => news.Pinned;

    public string Meta { get; } = NewsDetailViewModel.MetaOf(news);

    public string? Category => news.CategoryName?.ToUpper(CultureInfo.CurrentCulture);

    public bool HasCategory => news.CategoryName is not null;

    public ImageSource? Cover { get; } = news.Cover is null ? null : new BitmapImage(news.Cover) { DecodePixelWidth = 640 };

    public bool HasCover => news.Cover is not null;

    public bool HasNoCover => news.Cover is null;
}

/// <summary>Ein geöffneter Artikel.</summary>
public sealed partial class NewsDetailViewModel(AppServices services, NewsService news) : LoadableViewModel(services)
{
    private string? _slug;

    public string? Slug => _slug;

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial string? Meta { get; set; }

    [ObservableProperty]
    public partial string? Category { get; set; }

    [ObservableProperty]
    public partial ImageSource? Cover { get; set; }

    [ObservableProperty]
    public partial string? CoverAlt { get; set; }

    [ObservableProperty]
    public partial RichDocument? Body { get; set; }

    [ObservableProperty]
    public partial Uri? WebUrl { get; set; }

    [ObservableProperty]
    public partial string? Feedback { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    public ObservableCollection<CommentViewModel> Comments { get; } = [];

    [ObservableProperty]
    public partial string? CommentsHeader { get; set; }

    [ObservableProperty]
    public partial bool HasComments { get; set; }

    /// <summary>Mindestens ein Kommentar einer blockierten Person ist ausgeblendet.</summary>
    [ObservableProperty]
    public partial bool HasBlockedComments { get; set; }

    private bool _showBlocked;

    protected override bool HasData => Title is not null;

    public Task ShowAsync(string slug)
    {
        if (slug == _slug && Title is not null)
        {
            return Task.CompletedTask;
        }

        _slug = slug;
        _showBlocked = false;
        Title = null;
        Body = null;
        Comments.Clear();
        HasComments = false;
        return LoadAsync();
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_slug is null)
        {
            return;
        }

        var article = await news.GetAsync(_slug, cancellationToken);
        var s = article.Summary;
        Title = s.Title;
        Meta = MetaOf(s);
        Category = s.CategoryName?.ToUpper(CultureInfo.CurrentCulture);
        Cover = s.Cover is null ? null : new BitmapImage(s.Cover);
        CoverAlt = s.CoverAlt ?? s.Title;
        Body = article.Body;
        WebUrl = s.WebUrl;

        await LoadCommentsAsync(article.CommentsEnabled, cancellationToken);
    }

    private async Task LoadCommentsAsync(bool enabled, CancellationToken cancellationToken)
    {
        Comments.Clear();
        if (enabled && _slug is not null)
        {
            try
            {
                var hidden = Services.Localizer.GetString(StringKeys.NewsCommentBlocked);
                foreach (var c in await news.GetCommentsAsync(_slug, _showBlocked, cancellationToken))
                {
                    var blockedHidden = c.AuthorBlocked && c.Body is null;
                    Comments.Add(new CommentViewModel(c.Id, c.AuthorName, c.CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? string.Empty,
                        blockedHidden ? hidden : c.Body ?? string.Empty, Services.CurrentUser is not null && !blockedHidden, blockedHidden));
                }
            }
            catch (Exception ex) when (ex is RoG.Core.Api.ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
            {
                // Kommentare sind Beiwerk – der Artikel bleibt sichtbar
            }
        }

        HasComments = Comments.Count > 0;
        HasBlockedComments = Comments.Any(c => c.IsHidden);
        CommentsHeader = Format(StringKeys.NewsComments, Comments.Count);
    }

    /// <summary>„Trotzdem anzeigen“: Kommentare blockierter Personen mit Text nachladen.</summary>
    [RelayCommand]
    private async Task ShowBlockedCommentsAsync()
    {
        _showBlocked = true;
        await LoadCommentsAsync(true, CancellationToken.None);
    }

    [RelayCommand]
    private Task OpenInBrowserAsync() => WebUrl is null ? Task.CompletedTask : Services.Browser.OpenAsync(WebUrl);

    [RelayCommand]
    private void CopyLink()
    {
        if (WebUrl is null)
        {
            return;
        }

        try
        {
            Services.Clipboard.SetText(WebUrl.ToString());
            Feedback = Services.Localizer.GetString(StringKeys.CommonLinkCopied);
        }
#pragma warning disable CA1031 // Zwischenablage kann gesperrt sein – nur melden
        catch (Exception)
#pragma warning restore CA1031
        {
            Feedback = Services.Localizer.GetString(StringKeys.CommonCopyFailed);
        }

        IsFeedbackOpen = false;
        IsFeedbackOpen = true;
    }

    /// <summary>„16.10.2026 · Wahke · 3 Min. Lesezeit“.</summary>
    internal static string MetaOf(NewsSummary s)
    {
        var parts = new List<string>();
        if (s.PublishedAt is { } at)
        {
            parts.Add(at.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));
        }

        if (s.AuthorName is not null)
        {
            parts.Add(s.AuthorName);
        }

        if (s.SquadName is not null)
        {
            parts.Add(s.SquadName);
        }

        if (s.ReadingMinutes > 0)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, App.Services.Localizer.GetString(StringKeys.NewsReadingMinutes), s.ReadingMinutes));
        }

        return string.Join(" · ", parts);
    }
}

/// <summary>Klasse statt record: der Command-Generator des Toolkits erzeugt in records nichts (XAML fände ReportCommand nicht).</summary>
public sealed partial class CommentViewModel(string id, string author, string time, string body, bool canReport, bool isHidden = false)
{
    /// <summary>Kommentar einer blockierten Person, Text ausgeblendet.</summary>
    public bool IsHidden => isHidden;

    public string Id => id;

    public string Author => author;

    public string Time => time;

    public string Body => body;

    public bool CanReport => canReport;

    [RelayCommand]
    private Task ReportAsync() => Controls.ReportDialog.ShowAsync("news_comment", id);
}
