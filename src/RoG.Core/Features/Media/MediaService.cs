using System.Security.Cryptography;
using RoG.Core.Api;
using RoG.Core.Api.Generated;
using File = System.IO.File;
using Stream = System.IO.Stream;

namespace RoG.Core.Features.Media;

public sealed record GalleryAlbum(string Id, string Slug, string Title, string? Description, string? SquadName, int Items, Uri? Cover);

public sealed record GalleryItem(string Id, string Kind, string? Caption, string? Author, Uri? Thumb, Uri? Medium, Uri? Full, Uri? VideoUrl, Uri? WebUrl, DateTimeOffset? CreatedAt)
{
    public bool IsVideo => Kind != "image" && VideoUrl is not null;
}

public sealed record DownloadEntry(
    string Id,
    string Slug,
    string Title,
    string? Category,
    string? Game,
    int Downloads,
    string? VersionId,
    string? Version,
    string? Changelog,
    string? FileName,
    long Size,
    bool Available,
    Uri? WebUrl);

/// <summary>Galerie (Alben, Bilder/Clips) und Downloads (signierter Link, Prüfsumme).</summary>
public sealed class MediaService(IRoGApiClient client, HttpClient storage)
{
    public async Task<IReadOnlyList<GalleryAlbum>> GetAlbumsAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.V1_gallery_albums_indexAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Data.Select(a => new GalleryAlbum(a.Id, a.Slug, a.Title, ApiValues.Text(a.Description), ApiValues.Text(a.Squad), a.Items_count ?? 0, ApiValues.WebUri(a.Cover))).ToList();
    }

    /// <summary>Darf die Person in dieses Album einreichen (<c>can_upload</c>)?</summary>
    public async Task<bool> CanUploadAsync(string albumSlug, CancellationToken cancellationToken = default) =>
        (await client.V1_gallery_albums_showAsync(albumSlug, cancellationToken).ConfigureAwait(false)).Data.Can_upload;

    public Task<IReadOnlyList<GalleryItem>> GetItemsAsync(string albumSlug, CancellationToken cancellationToken = default) =>
        QueryItemsAsync(albumSlug, null, 200, cancellationToken);

    /// <summary>Neueste Einträge aller Alben eines Squads (Squad-Seite).</summary>
    public Task<IReadOnlyList<GalleryItem>> GetSquadItemsAsync(string squadSlug, int limit = 24, CancellationToken cancellationToken = default) =>
        QueryItemsAsync(null, squadSlug, limit, cancellationToken);

    private async Task<IReadOnlyList<GalleryItem>> QueryItemsAsync(string? albumSlug, string? squadSlug, int limit, CancellationToken cancellationToken)
    {
        var response = await client.V1_gallery_items_indexAsync(limit: limit, filtersquad: squadSlug, filteralbum: albumSlug, cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Data
            .Where(i => i.Status is null or "published" or "approved" or "ready")
            .Select(i => new GalleryItem(
                i.Id,
                i.Kind,
                ApiValues.Text(i.Caption),
                i.User?.Name,
                ApiValues.WebUri(i.Image?.Thumb ?? i.Poster_url),
                ApiValues.WebUri(i.Image?.Medium ?? i.Poster_url),
                ApiValues.WebUri(i.Image?.Full),
                ApiValues.WebUri(i.Video_url),
                ApiValues.WebUri(i.Web_url),
                ApiValues.Time(i.Created_at)))
            .ToList();
    }

    public async Task<IReadOnlyList<DownloadEntry>> GetDownloadsAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.V1_downloads_indexAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Data.Select(d => new DownloadEntry(
            d.Id,
            d.Slug,
            d.Title,
            d.Category?.Name,
            d.Game_info?.Name ?? ApiValues.Text(d.Game),
            d.Downloads_count,
            d.Latest?.Id,
            d.Latest?.Version,
            ApiValues.Text(d.Latest?.Changelog),
            d.Latest?.Filename,
            d.Latest?.Size ?? 0,
            d.Latest is { Status: "ready" or "published" } l && l.Scan_status is not ("infected" or "failed"),
            ApiValues.WebUri(d.Url))).ToList();
    }

    /// <summary>
    /// Version laden: signierten Link holen, in <paramref name="targetFolder"/> speichern (Name aus der API, bei Kollision
    /// „Name (2).ext“), Prüfsumme vergleichen. Stimmt sie nicht, wird die Datei gelöscht.
    /// </summary>
    public async Task<string> DownloadAsync(string versionId, string targetFolder, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var link = (await client.V1_downloads_versions_linkAsync(versionId, cancellationToken).ConfigureAwait(false)).Data;
        var url = ApiValues.WebUri(link.Url) ?? throw new ApiProblemException(new ApiProblem { Code = "download_failed", Status = 0 });
        Directory.CreateDirectory(targetFolder);
        var path = FreePath(targetFolder, SafeFileName(link.Filename));

        using (var response = await storage.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ApiProblemException(new ApiProblem { Code = "download_failed", Status = (int)response.StatusCode });
            }

            var total = response.Content.Headers.ContentLength ?? link.Size;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = File.Create(path);
            await CopyAsync(source, target, total, progress, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(link.Sha256) && !await HashMatchesAsync(path, link.Sha256, cancellationToken).ConfigureAwait(false))
        {
            File.Delete(path);
            throw new ApiProblemException(new ApiProblem { Code = "checksum_mismatch", Status = 0 });
        }

        return path;
    }

    internal static async Task<bool> HashMatchesAsync(string path, string expectedHex, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Nur der Dateiname, ohne Pfadteile und ungültige Zeichen (der Name kommt vom Server).</summary>
    internal static string SafeFileName(string? name)
    {
        // Selbst trennen statt Path.GetFileName: Unter Windows gilt „a:b“ sonst als Laufwerk und der Name wird zu „b“
        var raw = name ?? string.Empty;
        var file = raw[(raw.LastIndexOfAny(['/', '\\']) + 1)..];
        foreach (var c in Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', '*', '?', '"', '<', '>', '|']))
        {
            file = file.Replace(c, '_');
        }

        file = file.Trim().TrimStart('.');
        return file.Length == 0 ? "download" : file;
    }

    internal static string FreePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; File.Exists(path); i++)
        {
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
        }

        return path;
    }

    private static async Task CopyAsync(Stream source, Stream target, long total, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            done += read;
            progress?.Report(total > 0 ? (double)done / total : 0);
        }
    }
}
