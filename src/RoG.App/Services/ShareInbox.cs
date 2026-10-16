using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Storage;

namespace RoG.App.Services;

/// <summary>
/// Teilen-Ziel „An RoG hochladen“: geteilte Dateien/Bilder landen in einem Eingangsordner (lokaler Cache des Pakets),
/// der Teilen-Dialog wird sofort beendet. Der Uploader holt sie dort ab – auch wenn die Aktivierung von einem zweiten
/// Prozess an die laufende Instanz weitergereicht wurde.
/// </summary>
public static class ShareInbox
{
    private static string Folder => Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "share-inbox");

    public static async Task AcceptAsync(ShareOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.ReportStarted();
        try
        {
            var target = await StorageFolder.GetFolderFromPathAsync(Directory.CreateDirectory(Folder).FullName);
            var data = operation.Data;
            if (data.Contains(StandardDataFormats.StorageItems))
            {
                foreach (var item in await data.GetStorageItemsAsync())
                {
                    if (item is StorageFile file)
                    {
                        await file.CopyAsync(target, file.Name, NameCollisionOption.GenerateUniqueName);
                    }
                }
            }
            else if (data.Contains(StandardDataFormats.Bitmap))
            {
                var bitmap = await data.GetBitmapAsync();
                using var source = await bitmap.OpenReadAsync();
                var file = await target.CreateFileAsync($"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png", CreationCollisionOption.GenerateUniqueName);
                using var destination = await file.OpenAsync(FileAccessMode.ReadWrite);
                await Windows.Storage.Streams.RandomAccessStream.CopyAndCloseAsync(source.GetInputStreamAt(0), destination.GetOutputStreamAt(0));
            }

            operation.ReportCompleted();
        }
#pragma warning disable CA1031 // Teilen darf die App nie abstürzen lassen
        catch (Exception ex)
#pragma warning restore CA1031
        {
            operation.ReportError(ex.Message);
        }
    }

    /// <summary>Wartende geteilte Dateien (älteste zuerst).</summary>
    public static async Task<IReadOnlyList<StorageFile>> PendingAsync()
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        var files = new List<StorageFile>();
        foreach (var path in Directory.EnumerateFiles(Folder).OrderBy(File.GetCreationTimeUtc))
        {
            files.Add(await StorageFile.GetFileFromPathAsync(path));
        }

        return files;
    }

    /// <summary>Nach dem Hochladen (auch bei Fehlern) aufräumen – geteilte Dateien bleiben nicht liegen.</summary>
    public static void Clear()
    {
        try
        {
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // gesperrt – beim nächsten Mal
        }
    }
}
