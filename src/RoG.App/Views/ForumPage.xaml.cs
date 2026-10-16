using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RoG.App.ViewModels;
using RoG.Core.Api;
using RoG.Core.Content;
using RoG.Core.Localization;

namespace RoG.App.Views;

public sealed partial class ForumPage : Page
{
    public ForumPage()
    {
        ViewModel = new ForumViewModel(App.Services);
        InitializeComponent();
        ViewModel.Thread.DraftReplaced += (_, _) =>
        {
            ReplyBox.Focus(FocusState.Programmatic);
            ReplyBox.SelectionStart = ReplyBox.Text.Length;
        };
    }

    public ForumViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string { Length: > 0 } key)
        {
            await ViewModel.OpenAsync(key);
        }
        else
        {
            await ViewModel.LoadAsync();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.Dispose();
        base.OnNavigatedFrom(e);
    }

    private void OnCategoryClick(object sender, ItemClickEventArgs e) => ViewModel.Category = e.ClickedItem as CategoryItemViewModel;

    private void OnDetailClosed(object? sender, EventArgs e)
    {
        ViewModel.SelectedThread = null;
        ViewModel.ThreadForced = false;
    }

    /// <summary>BBCode-Leiste: Tag um die Markierung im Antwortfeld.</summary>
    private void OnFormatClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        var edit = BbCode.Wrap(ReplyBox.Text, ReplyBox.SelectionStart, ReplyBox.SelectionLength, tag);
        ViewModel.Thread.Draft = edit.Text;
        ReplyBox.Text = edit.Text;
        ReplyBox.Focus(FocusState.Programmatic);
        ReplyBox.Select(edit.SelectionStart, edit.SelectionLength);
    }

    private void OnSendInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ViewModel.Thread.SendCommand.CanExecute(null))
        {
            ViewModel.Thread.SendCommand.Execute(null);
        }
    }

    /// <summary>Neues Thema: Titel und Text im Dialog.</summary>
    private async void OnNewThreadClick(object sender, RoutedEventArgs e)
    {
        var l = App.Services.Localizer;
        var title = new TextBox { Header = l.GetString(StringKeys.ForumNewThreadTitle), MaxLength = 120 };
        var body = new TextBox { Header = l.GetString(StringKeys.ForumNewThreadBody), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 160 };
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        panel.Children.Add(title);
        panel.Children.Add(body);
        panel.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = l.GetString(StringKeys.ForumNewThread),
            Content = panel,
            PrimaryButtonText = l.GetString(StringKeys.ForumCreate),
            CloseButtonText = l.GetString(StringKeys.CommonCancel),
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (d, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(body.Text))
                {
                    args.Cancel = true;
                    return;
                }

                await ViewModel.CreateThreadAsync(title.Text, body.Text);
            }
            catch (ApiProblemException ex)
            {
                args.Cancel = true;
                error.Message = l.GetError(ex.Problem.Code);
                error.IsOpen = true;
            }
            catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or ArgumentException)
            {
                args.Cancel = true;
                error.Message = l.GetError(ApiProblem.Codes.Unknown);
                error.IsOpen = true;
            }
            finally
            {
                deferral.Complete();
            }
        };
        await dialog.ShowAsync();
    }

    private async void OnRefreshInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.LoadAsync();
    }
}
