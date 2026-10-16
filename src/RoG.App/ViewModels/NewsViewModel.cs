using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RoG.App.Services;
using RoG.Core.Content;
using RoG.Core.Features.News;
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
            foreach (var item in page.Items)
            {
                Items.Add(new NewsItemViewModel(item));
            }

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
public sealed class NewsItemViewModel(NewsSummary news)
{
    public string Slug => news.Slug;

    public string Title => news.Title;

    public string Teaser => news.Teaser;

    public bool Pinned => news.Pinned;

    public string Meta { get; } = NewsDetailViewModel.MetaOf(news);

    public string? Category => news.CategoryName?.ToUpper(CultureInfo.CurrentCulture);

    public bool HasCategory => news.CategoryName is not null;

    public ImageSource? Cover { get; } = news.Cover is null ? null : new BitmapImage(news.Cover) { DecodePixelWidth = 640 };

    public bool HasCover => news.Cover is not null;
}

/// <summary>Ein geöffneter Artikel.</summary>
public sealed partial class NewsDetailViewModel(AppServices services, NewsService news) : LoadableViewModel(services)
{
    private string? _slug;

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

    protected override bool HasData => Title is not null;

    public Task ShowAsync(string slug)
    {
        if (slug == _slug && Title is not null)
        {
            return Task.CompletedTask;
        }

        _slug = slug;
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

        Comments.Clear();
        if (article.CommentsEnabled)
        {
            try
            {
                foreach (var c in await news.GetCommentsAsync(_slug, cancellationToken))
                {
                    Comments.Add(new CommentViewModel(c.Id, c.AuthorName, c.CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? string.Empty, c.Body, Services.CurrentUser is not null));
                }
            }
            catch (Exception ex) when (ex is RoG.Core.Api.ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
            {
                // Kommentare sind Beiwerk – der Artikel bleibt sichtbar
            }
        }

        HasComments = Comments.Count > 0;
        CommentsHeader = Format(StringKeys.NewsComments, Comments.Count);
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

public sealed partial record CommentViewModel(string Id, string Author, string Time, string Body, bool CanReport)
{
    [RelayCommand]
    private Task ReportAsync() => Controls.ReportDialog.ShowAsync("news_comment", Id);
}
