using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Content;
using RoG.Core.Features.Forum;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Forum: links Bereiche bzw. Themen eines Bereichs, rechts das Thema mit Beiträgen und Antwort.</summary>
public sealed partial class ForumViewModel : LoadableViewModel, IDisposable
{
    private readonly ForumService _forum;
    private string? _threadCursor;

    public ForumViewModel(AppServices services)
        : base(services)
    {
        _forum = new ForumService(services.Api.Client, services.Environment.WebBaseUrl);
        Thread = new ThreadViewModel(services, _forum);
    }

    public ObservableCollection<CategoryItemViewModel> Categories { get; } = [];

    public ObservableCollection<ThreadItemViewModel> Threads { get; } = [];

    public ThreadViewModel Thread { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCategories), nameof(ShowThreads), nameof(CanCreateThread))]
    public partial CategoryItemViewModel? Category { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial ThreadItemViewModel? SelectedThread { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial bool ThreadForced { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingThreads { get; set; }

    [ObservableProperty]
    public partial bool HasMoreThreads { get; set; }

    [ObservableProperty]
    public partial bool IsSignedIn { get; set; }

    public bool ShowCategories => Category is null;

    public bool ShowThreads => Category is not null;

    public bool CanCreateThread => IsSignedIn && Category is { CanWrite: true };

    public bool IsDetailOpen => SelectedThread is not null || ThreadForced;

    public bool ShowSelectHint => !IsDetailOpen;

    protected override bool HasData => Categories.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IsSignedIn = await Services.Tokens.GetAsync(cancellationToken) is not null;
        var categories = await _forum.GetCategoriesAsync(cancellationToken);
        Categories.Clear();
        foreach (var c in categories.Where(c => c.ParentId is null))
        {
            Categories.Add(new CategoryItemViewModel(c, 0, Services.Localizer));
            foreach (var child in categories.Where(x => x.ParentId == c.Id))
            {
                Categories.Add(new CategoryItemViewModel(child, 1, Services.Localizer));
            }
        }

        OnPropertyChanged(nameof(CanCreateThread));
    }

    /// <summary>Thema direkt öffnen (Link <c>forum/…</c>; letzter Pfadteil = Themen-ID oder -Slug).</summary>
    public async Task OpenAsync(string key)
    {
        await LoadAsync();
        var id = key.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (id is not null)
        {
            ThreadForced = true;
            await Thread.ShowAsync(id);
        }
    }

    partial void OnCategoryChanged(CategoryItemViewModel? value)
    {
        Threads.Clear();
        if (value is not null)
        {
            _ = LoadThreadsAsync(value, null);
        }
    }

    private async Task LoadThreadsAsync(CategoryItemViewModel category, string? cursor)
    {
        IsLoadingThreads = cursor is null;
        try
        {
            var page = await _forum.GetThreadsAsync(category.Slug, cursor);
            foreach (var t in page.Items)
            {
                Threads.Add(new ThreadItemViewModel(t, Services.Localizer));
            }

            _threadCursor = page.NextCursor;
            HasMoreThreads = page.HasMore;
        }
        catch (ApiProblemException ex)
        {
            ErrorMessage = Services.Localizer.GetError(ex.Problem.Code);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            ErrorMessage = Services.Localizer.GetError(ApiProblem.Codes.Unknown);
        }
        finally
        {
            IsLoadingThreads = false;
        }
    }

    [RelayCommand]
    private Task LoadMoreThreadsAsync() => Category is { } c && _threadCursor is { } cursor ? LoadThreadsAsync(c, cursor) : Task.CompletedTask;

    [RelayCommand]
    private void BackToCategories()
    {
        Category = null;
        SelectedThread = null;
    }

    partial void OnSelectedThreadChanged(ThreadItemViewModel? value)
    {
        if (value is not null)
        {
            ThreadForced = false;
            value.MarkRead();
            _ = Thread.ShowAsync(value.Id);
        }
    }

    /// <summary>Neues Thema anlegen (Dialog auf der Seite); öffnet es danach.</summary>
    public async Task<string?> CreateThreadAsync(string title, string body)
    {
        if (Category is not { } category)
        {
            return null;
        }

        var id = await _forum.CreateThreadAsync(category.Slug, title, body);
        Threads.Clear();
        await LoadThreadsAsync(category, null);
        ThreadForced = true;
        await Thread.ShowAsync(id);
        return id;
    }

    public void Dispose() => Thread.Dispose();
}

public sealed class CategoryItemViewModel(ForumCategory category, int depth, ILocalizer localizer)
{
    public string Slug => category.Slug;

    public string Name => category.Name;

    public bool CanWrite => category.CanWrite && !category.Locked;

    public string Description => category.Description ?? string.Empty;

    public bool HasDescription => category.Description is not null;

    public string Stats { get; } = string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.ForumCategoryStats), category.Threads, category.Posts)
        + (category.SquadName is null ? string.Empty : " · " + category.SquadName);

    public Thickness Indent { get; } = new(depth * 20, 0, 0, 0);

    public bool Locked => category.Locked;
}

public sealed partial class ThreadItemViewModel(ForumThreadSummary thread, ILocalizer localizer) : ObservableObject
{
    public string Id => thread.Id;

    public string Title => thread.Title;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        thread.AuthorBlocked ? localizer.GetString(StringKeys.ForumBlockedAuthor) : thread.AuthorName,
        string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.ForumReplies), thread.Replies),
        thread.LastPostAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string Badges { get; } = string.Concat(thread.Pinned ? "📌 " : string.Empty, thread.Locked ? "🔒 " : string.Empty, thread.HasPoll ? "📊 " : string.Empty);

    public bool HasBadges => thread.Pinned || thread.Locked || thread.HasPoll;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleWeight), nameof(UnreadBrush))]
    public partial bool Unread { get; set; } = thread.Unread;

    public Windows.UI.Text.FontWeight TitleWeight => Unread ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;

    public Brush UnreadBrush => (Brush)Application.Current.Resources[Unread ? "RoGRedBrush" : "RoGBg2Brush"];

    public void MarkRead() => Unread = false;
}

/// <summary>Ein Thema: Beiträge, Umfrage, Reaktionen, Antwort mit BBCode-Leiste und Vorschau (Strg+Enter sendet).</summary>
public sealed partial class ThreadViewModel(AppServices services, ForumService forum) : LoadableViewModel(services), IDisposable
{
    private string? _threadId;
    private string? _postCursor;
    private IDisposable? _watch;
    private IReadOnlyList<ReactionOption>? _reactions;

    public ObservableCollection<PostViewModel> Posts { get; } = [];

    public ObservableCollection<PollOptionViewModel> PollOptions { get; } = [];

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial string? Meta { get; set; }

    [ObservableProperty]
    public partial bool CanReply { get; set; }

    [ObservableProperty]
    public partial bool HasMorePosts { get; set; }

    [ObservableProperty]
    public partial string? PollQuestion { get; set; }

    [ObservableProperty]
    public partial string? PollMeta { get; set; }

    [ObservableProperty]
    public partial bool HasPoll { get; set; }

    [ObservableProperty]
    public partial bool CanVote { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Draft { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsSending { get; set; }

    [ObservableProperty]
    public partial RichDocument? PreviewDocument { get; set; }

    [ObservableProperty]
    public partial bool ShowPreview { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    /// <summary>Text in das Antwortfeld einfügen (Zitat) – die Ansicht setzt die Eingabemarke.</summary>
    public event EventHandler? DraftReplaced;

    protected override bool HasData => Title is not null;

    public IReadOnlyList<ReactionOption> ReactionOptions => _reactions ?? [];

    public Task ShowAsync(string threadId)
    {
        if (threadId == _threadId && Title is not null)
        {
            return Task.CompletedTask;
        }

        _watch?.Dispose();
        _watch = null;
        _threadId = threadId;
        Title = null;
        Posts.Clear();
        Draft = string.Empty;
        ShowPreview = false;
        return LoadAsync();
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_threadId is null)
        {
            return;
        }

        var threadTask = forum.GetThreadAsync(_threadId, cancellationToken);
        var postsTask = forum.GetPostsAsync(_threadId, null, cancellationToken);
        _reactions ??= await TryReactionsAsync(cancellationToken);
        var thread = await threadTask;
        var posts = await postsTask;
        var signedIn = await Services.Tokens.GetAsync(cancellationToken) is not null;

        Meta = string.Join(" · ", new[] { thread.CategoryName, thread.Summary.AuthorName }.Where(s => !string.IsNullOrEmpty(s)));
        CanReply = signedIn && thread.CanReply;
        ApplyPoll(thread.Poll, signedIn);

        Posts.Clear();
        foreach (var p in posts.Items)
        {
            Posts.Add(new PostViewModel(p, this, signedIn));
        }

        _postCursor = posts.NextCursor;
        HasMorePosts = posts.HasMore;
        Title = thread.Summary.Title;
        _watch ??= Services.Realtime.WatchChannel($"private-forum.threads.{_threadId}", (name, _) =>
        {
            if (name == "post.published")
            {
                _ = LoadAsync(silent: true);
            }
        });
    }

    private async Task<IReadOnlyList<ReactionOption>?> TryReactionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await forum.GetReactionsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            return null;
        }
    }

    private void ApplyPoll(ForumPoll? poll, bool signedIn)
    {
        PollOptions.Clear();
        HasPoll = poll is not null;
        if (poll is null)
        {
            return;
        }

        PollQuestion = poll.Question;
        PollMeta = Format(StringKeys.ForumPollVoters, poll.Voters) + (poll.Closed ? " · " + Services.Localizer.GetString(StringKeys.ForumPollClosed) : string.Empty);
        var total = Math.Max(1, poll.Options.Sum(o => o.Votes));
        CanVote = signedIn && !poll.Closed;
        foreach (var o in poll.Options)
        {
            PollOptions.Add(new PollOptionViewModel(poll.Id, o, (double)o.Votes / total, this));
        }
    }

    [RelayCommand]
    private async Task LoadMorePostsAsync()
    {
        if (_threadId is null || _postCursor is null)
        {
            return;
        }

        try
        {
            var page = await forum.GetPostsAsync(_threadId, _postCursor);
            var signedIn = await Services.Tokens.GetAsync() is not null;
            foreach (var p in page.Items)
            {
                Posts.Add(new PostViewModel(p, this, signedIn));
            }

            _postCursor = page.NextCursor;
            HasMorePosts = page.HasMore;
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // „Mehr laden“ erneut
        }
    }

    private bool CanSend() => !IsSending && !string.IsNullOrWhiteSpace(Draft);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (_threadId is null)
        {
            return;
        }

        IsSending = true;
        try
        {
            await forum.ReplyAsync(_threadId, Draft);
            Draft = string.Empty;
            ShowPreview = false;
            Show(Services.Localizer.GetString(StringKeys.ForumReplySent), InfoBarSeverity.Success);
            await LoadAsync(silent: true);
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or ArgumentOutOfRangeException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private async Task TogglePreviewAsync()
    {
        if (ShowPreview)
        {
            ShowPreview = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(Draft))
        {
            return;
        }

        try
        {
            PreviewDocument = await forum.PreviewAsync(Draft);
            ShowPreview = true;
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    internal void QuotePost(PostViewModel post)
    {
        Draft = Draft + BbCode.Quote(post.Author, post.PlainText);
        DraftReplaced?.Invoke(this, EventArgs.Empty);
    }

    internal async Task ReactAsync(PostViewModel post, string key)
    {
        try
        {
            await forum.ToggleReactionAsync(post.Id, key);
            await LoadAsync(silent: true);
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    internal async Task VoteAsync(string pollId, string optionId)
    {
        try
        {
            await forum.VoteAsync(pollId, [optionId]);
            await LoadAsync(silent: true);
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    internal async Task<RichDocument?> RevealAsync(string postId)
    {
        try
        {
            return (await forum.GetPostAsync(postId)).Content;
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            return null;
        }
    }

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }

    public void Dispose()
    {
        _watch?.Dispose();
        _watch = null;
    }
}

public sealed partial class PostViewModel : ObservableObject
{
    private readonly ForumPost _post;
    private readonly ThreadViewModel _owner;

    public PostViewModel(ForumPost post, ThreadViewModel owner, bool signedIn)
    {
        _post = post;
        _owner = owner;
        Content = post.Content;
        CanInteract = signedIn && !post.Pending && !post.Hidden;
        Reactions = post.Reactions.Select(r => new ReactionChipViewModel(r, this)).ToList();
        ReactionChoices = owner.ReactionOptions.Select(o => new ReactionChoiceViewModel(o, this)).ToList();
    }

    public string Id => _post.Id;

    public string Author => _post.AuthorBlocked ? App.Services.Localizer.GetString(StringKeys.ForumBlockedAuthor) : _post.AuthorName;

    public string Time { get; } = string.Empty;

    public string Meta => string.Join(" · ", new[]
    {
        _post.CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        _post.EditedAt is not null ? App.Services.Localizer.GetString(StringKeys.ForumEdited) : null,
        _post.Pending ? App.Services.Localizer.GetString(StringKeys.ForumPending) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    [ObservableProperty]
    public partial RichDocument? Content { get; set; }

    public bool IsCollapsed => _post.AuthorBlocked && Content is { Blocks.Count: 0 };

    public bool CanInteract { get; }

    public string PlainText => Content?.ToPlainText() ?? string.Empty;

    public IReadOnlyList<ReactionChipViewModel> Reactions { get; }

    public bool HasReactions => Reactions.Count > 0;

    public IReadOnlyList<ReactionChoiceViewModel> ReactionChoices { get; }

    public string Attachments => string.Join(", ", _post.Attachments);

    public bool HasAttachments => _post.Attachments.Count > 0;

    [RelayCommand]
    private void Quote() => _owner.QuotePost(this);

    [RelayCommand]
    private async Task RevealAsync()
    {
        if (await _owner.RevealAsync(Id) is { } doc)
        {
            Content = doc;
            OnPropertyChanged(nameof(IsCollapsed));
        }
    }

    internal Task ReactAsync(string key) => _owner.ReactAsync(this, key);
}

public sealed partial class ReactionChipViewModel(PostReaction reaction, PostViewModel post)
{
    public string Text { get; } = $"{reaction.Emoji} {reaction.Count}";

    public Brush BorderBrush { get; } = (Brush)Application.Current.Resources[reaction.Mine ? "RoGRedBrush" : "RoGLineBrush"];

    [RelayCommand]
    private Task ToggleAsync() => post.ReactAsync(reaction.Key);
}

public sealed partial class ReactionChoiceViewModel(ReactionOption option, PostViewModel post)
{
    public string Text { get; } = $"{option.Emoji} {option.Label}";

    [RelayCommand]
    private Task ChooseAsync() => post.ReactAsync(option.Key);
}

public sealed partial class PollOptionViewModel(string pollId, PollOption option, double share, ThreadViewModel owner)
{
    public string Label { get; } = option.Mine ? $"✓ {option.Label}" : option.Label;

    public double Percent { get; } = Math.Round(share * 100);

    public string Votes { get; } = $"{option.Votes} ({Math.Round(share * 100)} %)";

    [RelayCommand]
    private Task VoteAsync() => owner.VoteAsync(pollId, option.Id);
}
