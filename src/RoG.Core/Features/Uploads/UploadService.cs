using System.Net.Http.Headers;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Api.Generated;
using Stream = System.IO.Stream;

namespace RoG.Core.Features.Uploads;

/// <summary>Sichtbarkeit eines Uploads (Guide 20).</summary>
public enum UploadVisibility
{
    Public,
    Members,
    Private,
}

public sealed record UploadLinks(Uri? Direct, Uri? Preview, Uri? Thumb, string? BbCode, string? Markdown, string? Html);

public sealed record UploadItem(
    string Id,
    string Code,
    string Status,
    string? Title,
    string OriginalName,
    string Kind,
    long Size,
    UploadVisibility Visibility,
    DateTimeOffset? CreatedAt,
    UploadLinks Links,
    string? ErrorMessage)
{
    public bool IsReady => Status == "ready";

    public bool Failed => Status == "failed";
}

/// <summary>
/// Uploader (Guide 20) über den pre-signed Weg: <c>POST /v1/uploads/presign</c> → Datei per <c>PUT</c> direkt an den Speicher
/// (eigener <see cref="HttpClient"/> ohne Token – der Geräte-Token geht nie an den Speicher) → <c>POST …/complete</c>.
/// EXIF/GPS entfernt der Server; erlaubt sind Bilder und kurze Videos.
/// </summary>
public sealed class UploadService(IRoGApiClient client, HttpClient storage)
{
    private static readonly Dictionary<string, string> s_mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
        [".avif"] = "image/avif",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".mov"] = "video/quicktime",
        [".mkv"] = "video/x-matroska",
    };

    /// <summary>MIME-Typ aus der Dateiendung; <c>null</c> = nicht hochladbar.</summary>
    public static string? MimeFor(string fileName) => s_mimes.TryGetValue(Path.GetExtension(fileName), out var mime) ? mime : null;

    public async Task<UploadItem> UploadAsync(
        Stream content,
        string fileName,
        long size,
        UploadVisibility visibility,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var uploadId = await PutAsync(content, fileName, size, visibility, progress, cancellationToken).ConfigureAwait(false);
        var completed = await client.V1_uploads_completeAsync(uploadId, cancellationToken).ConfigureAwait(false);
        return Map(JsonSerializer.SerializeToElement(completed.Data));
    }

    /// <summary>
    /// In ein Galerie-Album einreichen (Guide 24 „Große Dateien“): presign + Datei hochladen, dann ohne <c>complete</c>
    /// <c>POST /v1/gallery/albums/{slug}/items</c> mit <c>upload_id</c>. Rückgabe: <c>pending</c> (Freigabe nötig) oder <c>approved</c>.
    /// </summary>
    public async Task<string> SubmitToGalleryAsync(
        string albumSlug,
        Stream content,
        string fileName,
        long size,
        string? caption,
        bool rightsConfirmed,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!rightsConfirmed)
        {
            throw new ArgumentException("Rechte müssen bestätigt sein", nameof(rightsConfirmed));
        }

        var uploadId = await PutAsync(content, fileName, size, UploadVisibility.Private, progress, cancellationToken).ConfigureAwait(false);
        var response = await client.V1_gallery_items_storeAsync(albumSlug, new Body44
        {
            Upload_id = uploadId,
            Rights_confirmed = true,
            Caption = ApiValues.Text(caption),
        }, cancellationToken).ConfigureAwait(false);
        var status = JsonSerializer.SerializeToElement(response.Data);
        return status.ValueKind == JsonValueKind.Object && status.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
            ? st.GetString() ?? "pending"
            : "pending";
    }

    /// <summary>presign + Datei direkt in den Speicher (ohne Token); Rückgabe = Upload-ID.</summary>
    private async Task<string> PutAsync(Stream content, string fileName, long size, UploadVisibility visibility, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var mime = MimeFor(fileName) ?? throw new NotSupportedException(fileName);
        var presign = (await client.V1_uploads_presignAsync(new Body15
        {
            Filename = Path.GetFileName(fileName),
            Size = checked((int)size),
            Mime = mime,
            Visibility = VisibilityValue(visibility),
        }, cancellationToken).ConfigureAwait(false)).Data;

        using var request = new HttpRequestMessage(new HttpMethod(string.IsNullOrEmpty(presign.Method) ? "PUT" : presign.Method.ToUpperInvariant()), presign.Upload_url);
        request.Content = new ProgressContent(content, size, progress);
        foreach (var (name, value) in presign.Headers ?? new Dictionary<string, object>())
        {
            var text = value is JsonElement e ? (e.ValueKind == JsonValueKind.Array ? e[0].ToString() : e.ToString()) : value?.ToString();
            if (text is null)
            {
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(name, text))
            {
                request.Content.Headers.TryAddWithoutValidation(name, text);
            }
        }

        using var response = await storage.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiProblemException(new ApiProblem { Code = "upload_failed", Status = (int)response.StatusCode });
        }

        return presign.Upload.Id;
    }

    public async Task<Page<UploadItem>> ListAsync(string? cursor = null, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_uploads_indexAsync(cursor: cursor, per_page: 40, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new Page<UploadItem>(response.Data.Select(d => Map(JsonSerializer.SerializeToElement(d))).ToList(), response.Meta?.Next_cursor);
    }

    public async Task<UploadItem> GetAsync(string id, CancellationToken cancellationToken = default) =>
        Map(JsonSerializer.SerializeToElement((await client.V1_uploads_showAsync(id, cancellationToken).ConfigureAwait(false)).Data));

    public static string VisibilityValue(UploadVisibility v) => v switch
    {
        UploadVisibility.Members => "members",
        UploadVisibility.Private => "private",
        _ => "public",
    };

    /// <summary>Upload aus JSON (Liste, Detail und complete haben dieselbe Form, aber verschiedene generierte Typen).</summary>
    internal static UploadItem Map(JsonElement e)
    {
        static string? S(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        // complete liefert ggf. {upload: …}
        if (e.TryGetProperty("upload", out var inner) && inner.ValueKind == JsonValueKind.Object)
        {
            e = inner;
        }

        var links = e.TryGetProperty("links", out var l) ? l : default;
        var error = e.TryGetProperty("error", out var err) ? err : default;
        var size = e.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetInt64() : 0;
        return new UploadItem(
            S(e, "id") ?? string.Empty,
            S(e, "code") ?? string.Empty,
            S(e, "status") ?? "processing",
            ApiValues.Text(S(e, "title")),
            S(e, "original_name") ?? string.Empty,
            S(e, "kind") ?? "image",
            size,
            S(e, "visibility") switch
            {
                "members" => UploadVisibility.Members,
                "private" => UploadVisibility.Private,
                _ => UploadVisibility.Public,
            },
            ApiValues.Time(S(e, "created_at")),
            new UploadLinks(
                ApiValues.WebUri(S(links, "direct")),
                ApiValues.WebUri(S(links, "preview")),
                ApiValues.WebUri(S(links, "thumb")),
                S(links, "bbcode"),
                S(links, "markdown"),
                S(links, "html")),
            S(error, "message"));
    }

    /// <summary>Stream-Inhalt mit Fortschrittsmeldung (0…1).</summary>
    private sealed class ProgressContent(Stream source, long length, IProgress<double>? progress) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            var buffer = new byte[81920];
            long sent = 0;
            int read;
            while ((read = await source.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                sent += read;
                progress?.Report(length > 0 ? (double)sent / length : 0);
            }
        }

        protected override bool TryComputeLength(out long len)
        {
            len = length;
            return true;
        }
    }
}
