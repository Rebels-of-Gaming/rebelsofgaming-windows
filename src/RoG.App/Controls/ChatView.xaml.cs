using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using RoG.App.ViewModels;
using Windows.System;

namespace RoG.App.Controls;

/// <summary>Chat-Ansicht; die Seite bzw. das Fenster setzt <see cref="ViewModel"/> und ruft <c>LoadAsync</c>.</summary>
public sealed partial class ChatView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(ChatViewModel), typeof(ChatView), new PropertyMetadata(null, OnViewModelChanged));

    public ChatView() => InitializeComponent();

    public ChatViewModel? ViewModel
    {
        get => (ChatViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public void FocusComposer() => Composer.Focus(FocusState.Programmatic);

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ChatView)d;
        if (e.OldValue is ChatViewModel old)
        {
            old.MessageAppended -= view.OnMessageAppended;
        }

        if (e.NewValue is ChatViewModel vm)
        {
            vm.MessageAppended += view.OnMessageAppended;
        }

        view.Bindings.Update();
    }

    /// <summary>Nach neuer Nachricht ans Ende scrollen.</summary>
    private void OnMessageAppended(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (ViewModel is { Messages.Count: > 0 } vm)
        {
            MessageList.ScrollIntoView(vm.Messages[^1]);
        }
    });

    /// <summary>Enter sendet, Umschalt+Enter macht eine neue Zeile.</summary>
    private void OnComposerKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (!shift)
        {
            e.Handled = true;
            if (ViewModel?.SendCommand.CanExecute(null) == true)
            {
                ViewModel.SendCommand.Execute(null);
            }
        }
    }

    private void OnAttachmentImageLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatAttachmentViewModel attachment })
        {
            _ = attachment.LoadPreviewAsync();
        }
    }
}
