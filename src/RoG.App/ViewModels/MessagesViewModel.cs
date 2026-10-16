using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RoG.App.Services;
using RoG.Core.Features.Messages;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Nachrichten: Unterhaltungen (Suche, ungelesen), Chat daneben; Liste aktualisiert sich bei neuen Nachrichten.</summary>
public sealed partial class MessagesViewModel : LoadableViewModel, IDisposable
{
    private readonly MessageService _messages;
    private int _searchVersion;

    public MessagesViewModel(AppServices services)
        : base(services)
    {
        _messages = new MessageService(services.Api.Client);
        services.Realtime.PropertyChanged += OnRealtimeChanged;
    }

    public ObservableCollection<ConversationItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConversations))]
    public partial bool IsGuest { get; set; }

    public bool ShowConversations => !IsGuest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial ConversationItemViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial ChatViewModel? Chat { get; set; }

    public bool IsDetailOpen => Selected is not null;

    public bool ShowSelectHint => Selected is null && !IsGuest;

    protected override bool HasData => Items.Count > 0;

    /// <summary>Unterhaltung direkt öffnen (Deep Link, „Nachricht senden“ im Profil).</summary>
    public async Task OpenAsync(string conversationId)
    {
        await LoadAsync();
        Selected = Items.FirstOrDefault(i => i.Id == conversationId);
        if (Selected is null)
        {
            OpenChat(conversationId, Services.Localizer.GetString(StringKeys.NavMessages));
        }
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IsGuest = await Services.Tokens.GetAsync(cancellationToken) is null;
        OnPropertyChanged(nameof(ShowSelectHint));
        if (IsGuest)
        {
            Items.Clear();
            return;
        }

        var page = await _messages.ListAsync(SearchText, null, cancellationToken);
        var selected = Selected?.Id;
        Items.Clear();
        foreach (var c in page.Items)
        {
            Items.Add(new ConversationItemViewModel(c));
        }

        if (selected is not null)
        {
            // Auswahl halten, ohne den offenen Chat neu zu laden
            var again = Items.FirstOrDefault(i => i.Id == selected);
            if (again is not null)
            {
                _keepChat = true;
                Selected = again;
                _keepChat = false;
            }
        }
    }

    private bool _keepChat;

    partial void OnSelectedChanged(ConversationItemViewModel? value)
    {
        if (value is not null && !_keepChat && Chat?.ConversationId != value.Id)
        {
            OpenChat(value.Id, value.Name);
            value.MarkRead();
        }
    }

    private void OpenChat(string id, string title)
    {
        Chat?.Dispose();
        Chat = new ChatViewModel(Services, id, title);
        _ = Chat.LoadAsync();
    }

    public void CloseChat()
    {
        Selected = null;
        Chat?.Dispose();
        Chat = null;
    }

    partial void OnSearchTextChanged(string? value) => _ = ReloadDelayedAsync();

    private async Task ReloadDelayedAsync()
    {
        var version = ++_searchVersion;
        await Task.Delay(350);
        if (version == _searchVersion)
        {
            await LoadAsync();
        }
    }

    /// <summary>Neue Nachricht irgendwo (Zähler geändert) → Liste still neu laden (Reihenfolge, Vorschau, ungelesen).</summary>
    private void OnRealtimeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RealtimeService.UnreadMessages))
        {
            _ = LoadAsync(silent: true);
        }
    }

    [RelayCommand]
    private void SignIn() => Services.RequestSignIn();

    public void Dispose()
    {
        Services.Realtime.PropertyChanged -= OnRealtimeChanged;
        Chat?.Dispose();
        Chat = null;
    }
}

public sealed partial class ConversationItemViewModel(ConversationSummary conversation) : ObservableObject
{
    public string Id => conversation.Id;

    public string Name => conversation.Name;

    public ImageSource? Avatar { get; } = conversation.Avatar is null ? null : new BitmapImage(conversation.Avatar) { DecodePixelWidth = 64 };

    public string Preview { get; } = conversation.LastMessagePreview is null
        ? string.Empty
        : conversation.Kind == ConversationKind.Direct || conversation.LastMessageAuthor is null
            ? conversation.LastMessagePreview
            : $"{conversation.LastMessageAuthor}: {conversation.LastMessagePreview}";

    public string Time { get; } = conversation.LastMessageAt?.ToLocalTime() is { } t
        ? t.ToString(t.Date == DateTime.Today ? "t" : "d", CultureInfo.CurrentCulture)
        : string.Empty;

    public string KindGlyph { get; } = conversation.Kind switch
    {
        ConversationKind.Group => "",
        ConversationKind.Squad => "",
        _ => string.Empty,
    };

    public bool HasKindGlyph => conversation.Kind != ConversationKind.Direct;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadBrush), nameof(NameWeight))]
    public partial bool Unread { get; set; } = conversation.Unread;

    public Brush UnreadBrush => (Brush)Application.Current.Resources[Unread ? "RoGRedBrush" : "RoGBg2Brush"];

    public Windows.UI.Text.FontWeight NameWeight => Unread ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.SemiBold;

    public void MarkRead() => Unread = false;
}
