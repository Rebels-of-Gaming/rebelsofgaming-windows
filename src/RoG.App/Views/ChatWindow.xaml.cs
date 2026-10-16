using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using RoG.App.ViewModels;

namespace RoG.App.Views;

/// <summary>Chat in eigenem Fenster; je Unterhaltung höchstens eins (erneutes Öffnen holt es nach vorn).</summary>
public sealed partial class ChatWindow : Window
{
    private static readonly Dictionary<string, ChatWindow> s_open = new(StringComparer.Ordinal);
    private readonly ChatViewModel _viewModel;

    private ChatWindow(string conversationId, string title)
    {
        InitializeComponent();
        _viewModel = new ChatViewModel(App.Services, conversationId, title);
        Chat.ViewModel = _viewModel;
        TitleText.Text = title.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        Title = title;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(460, 680));
        Closed += (_, _) =>
        {
            s_open.Remove(conversationId);
            _viewModel.Dispose();
        };
        _ = _viewModel.LoadAsync();
    }

    public static void Open(string conversationId, string title)
    {
        if (!s_open.TryGetValue(conversationId, out var window))
        {
            window = new ChatWindow(conversationId, title);
            s_open[conversationId] = window;
        }

        window.Activate();
        window.Chat.FocusComposer();
    }

    private void OnTopChanged(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = OnTopToggle.IsChecked == true;
        }
    }
}
