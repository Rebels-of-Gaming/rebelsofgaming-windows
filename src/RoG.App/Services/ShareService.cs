using Windows.ApplicationModel.DataTransfer;

namespace RoG.App.Services;

/// <summary>
/// Windows-Teilen-Dialog für Text (Desktop-App: <see cref="DataTransferManagerInterop"/> mit dem Fenster-Handle statt
/// CoreWindow). Ein Handler je Fenster, der Inhalt wird beim Anfordern übergeben.
/// </summary>
public static class ShareService
{
    private static (string Title, string Text)? s_pending;
    private static DataTransferManager? s_manager;

    public static void ShareText(string title, string text)
    {
        var hwnd = App.MainWindowHandle;
        if (hwnd == 0)
        {
            return;
        }

        if (s_manager is null)
        {
            s_manager = DataTransferManagerInterop.GetForWindow(hwnd);
            s_manager.DataRequested += OnRequested;
        }

        s_pending = (title, text);
        DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
    }

    private static void OnRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        if (s_pending is not { } pending)
        {
            return;
        }

        args.Request.Data.Properties.Title = pending.Title;
        args.Request.Data.SetText(pending.Text);
    }
}
