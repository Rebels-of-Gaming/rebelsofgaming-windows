using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Messages;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Ein Chat (Seite „Nachrichten“ und herausgelöstes Fenster): Verlauf mit „ältere laden“, live neue Nachrichten,
/// Senden (Enter; Umschalt+Enter = neue Zeile), Zitat-Antwort, gelesen markieren.
/// </summary>
public sealed partial class ChatViewModel : LoadableViewModel, IDisposable
{
    private readonly MessageService _messages;
    private IDisposable? _watch;
    private string? _olderCursor;
    private string? _myId;

    public ChatViewModel(AppServices services, string conversationId, string title)
        : base(services)
    {
        _messages = new MessageService(services.Api.Client);
        ConversationId = conversationId;
        Title = title;
    }

    public string ConversationId { get; }

    public string Title { get; }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Draft { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsSending { get; set; }

    [ObservableProperty]
    public partial bool HasOlder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuote))]
    public partial ChatMessageViewModel? QuoteTarget { get; set; }

    public bool HasQuote => QuoteTarget is not null;

    [ObservableProperty]
    public partial string? SendError { get; set; }

    [ObservableProperty]
    public partial bool IsSendErrorOpen { get; set; }

    /// <summary>Neue Nachricht am Ende – die Ansicht scrollt dann nach unten.</summary>
    public event EventHandler? MessageAppended;

    protected override bool HasData => Messages.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        _myId = Services.CurrentUser?.Id ?? (await Services.Tokens.GetAsync(cancellationToken))?.UserId;
        var page = await _messages.GetMessagesAsync(ConversationId, null, cancellationToken);
        Messages.Clear();
        foreach (var m in page.Items)
        {
            Messages.Add(new ChatMessageViewModel(m, m.AuthorId == _myId, this));
        }

        _olderCursor = page.NextCursor;
        HasOlder = page.HasMore;
        _watch ??= Services.Realtime.WatchConversation(ConversationId, OnLiveMessage, OnLiveDeleted);
        MessageAppended?.Invoke(this, EventArgs.Empty);
        await MarkReadAsync();
    }

    [RelayCommand]
    private async Task LoadOlderAsync()
    {
        if (_olderCursor is null)
        {
            return;
        }

        try
        {
            var page = await _messages.GetMessagesAsync(ConversationId, _olderCursor);
            for (var i = page.Items.Count - 1; i >= 0; i--)
            {
                Messages.Insert(0, new ChatMessageViewModel(page.Items[i], page.Items[i].AuthorId == _myId, this));
            }

            _olderCursor = page.NextCursor;
            HasOlder = page.HasMore;
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // erneut versuchen über den Knopf
        }
    }

    private bool CanSend() => !IsSending && !string.IsNullOrWhiteSpace(Draft) && Draft.Trim().Length <= MessageService.MaxLength;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Draft;
        var quote = QuoteTarget?.Message.Id;
        IsSending = true;
        IsSendErrorOpen = false;
        try
        {
            var sent = await _messages.SendAsync(ConversationId, text, quote);
            Draft = string.Empty;
            QuoteTarget = null;
            Append(sent);
        }
        catch (ApiProblemException ex) when (ex.Problem.IsNetworkError)
        {
            await Services.Outbox.EnqueueAsync(RoG.Core.Offline.OutboxKind.DirectMessage, ConversationId, new { body = text.Trim(), quote_id = quote }, Services.Localizer.GetString(StringKeys.OutboxLabelMessage));
            Draft = string.Empty;
            QuoteTarget = null;
            ShowError(Services.Localizer.GetString(StringKeys.OutboxQueued));
        }
        catch (ApiProblemException ex)
        {
            ShowError(Services.Localizer.GetError(ex.Problem.Code));
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            ShowError(Services.Localizer.GetError(ApiProblem.Codes.Unknown));
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private void ClearQuote() => QuoteTarget = null;

    internal void Quote(ChatMessageViewModel message) => QuoteTarget = message;

    private void ShowError(string text)
    {
        SendError = text;
        IsSendErrorOpen = true;
    }

    private void Append(ChatMessage message)
    {
        if (Messages.Any(m => m.Message.Id == message.Id))
        {
            return; // eigene Nachricht kommt zusätzlich live zurück
        }

        Messages.Add(new ChatMessageViewModel(message, message.AuthorId == _myId, this));
        DataChanged();
        MessageAppended?.Invoke(this, EventArgs.Empty);
    }

    private void OnLiveMessage(ChatMessage message)
    {
        Append(message);
        if (message.AuthorId != _myId)
        {
            _ = MarkReadAsync();
        }
    }

    private void OnLiveDeleted(string id)
    {
        var index = Messages.ToList().FindIndex(m => m.Message.Id == id);
        if (index >= 0)
        {
            var old = Messages[index];
            Messages[index] = new ChatMessageViewModel(old.Message with { Body = null, Deleted = true, Attachments = [] }, old.IsOwn, this);
        }
    }

    private async Task MarkReadAsync()
    {
        try
        {
            Services.Realtime.UnreadMessages = await _messages.MarkReadAsync(ConversationId);
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException)
        {
            // Zähler korrigiert sich beim nächsten Abgleich
        }
    }

    public void Dispose()
    {
        _watch?.Dispose();
        _watch = null;
    }
}

/// <summary>Eine Nachricht: eigene rechts, fremde links; gelöschte als Hinweis.</summary>
public sealed partial class ChatMessageViewModel(ChatMessage message, bool isOwn, ChatViewModel owner)
{
    public ChatMessage Message => message;

    public bool IsOwn => isOwn;

    public string Author => message.AuthorName;

    public bool ShowAuthor => !isOwn;

    public string Body { get; } = message.Deleted ? App.Services.Localizer.GetString(StringKeys.MessagesDeleted) : message.Body ?? string.Empty;

    public bool HasBody => message.Deleted || !string.IsNullOrEmpty(message.Body);

    public string Time { get; } = message.CreatedAt?.ToLocalTime().ToString(message.CreatedAt?.ToLocalTime().Date == DateTime.Today ? "t" : "g", CultureInfo.CurrentCulture) ?? string.Empty;

    public string Quote { get; } = message.QuoteBody is null ? string.Empty : $"{message.QuoteAuthor}: {message.QuoteBody}";

    public bool HasQuote => message.QuoteBody is not null;

    /// <summary>„Antwort an …“-Leiste über der Eingabe.</summary>
    public string ReplyPreview => $"↩ {Author}: {Body}";

    public string Attachments { get; } = string.Join(", ", message.Attachments.Select(a => a.Name));

    public bool HasAttachments => message.Attachments.Count > 0;

    public HorizontalAlignment Alignment => isOwn ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public Brush Background => (Brush)Application.Current.Resources[isOwn ? "RoGBg3Brush" : "RoGBg2Brush"];

    public Brush AccentBrush => (Brush)Application.Current.Resources[isOwn ? "RoGActionRedBrush" : "RoGLineBrush"];

    public double BodyOpacity => message.Deleted ? 0.6 : 1;

    public string AutomationName => $"{Author}, {Time}: {Body}";

    [RelayCommand]
    private void Reply() => owner.Quote(this);

    public bool CanReport => !isOwn && !message.Deleted;

    [RelayCommand]
    private Task ReportAsync() => Controls.ReportDialog.ShowAsync("message", message.Id);

    [RelayCommand]
    private void Copy()
    {
        try
        {
            App.Services.Clipboard.SetText(message.Body ?? string.Empty);
        }
#pragma warning disable CA1031 // Zwischenablage kann gesperrt sein
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
