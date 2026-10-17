using RoG.Core.Api;

namespace RoG.Core.Features.Messages;

/// <summary>Geladener Anhang: Bytes (nur Bilder bis <see cref="AttachmentService.MaxPreviewBytes"/>) und der kurzlebige Link.</summary>
public sealed record AttachmentContent(byte[] Bytes, Uri? Url);

/// <summary>
/// Anhänge von Nachrichten: <c>GET /v1/attachments/{id}</c> prüft das Recht und leitet auf einen kurzlebigen, signierten
/// Link um. Der Token geht bei der Umleitung nicht mit (HttpClient entfernt <c>Authorization</c> bei Redirects).
/// </summary>
public sealed class AttachmentService(HttpClient http)
{
    /// <summary>Größere Bilder werden nicht in der Unterhaltung vorgeschaut.</summary>
    public const long MaxPreviewBytes = 8 * 1024 * 1024;

    public static bool CanPreview(ChatAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return attachment.IsImage && attachment.Size is > 0 and <= MaxPreviewBytes;
    }

    /// <summary>Kurzlebigen Link holen (nur Kopfzeilen, der Inhalt wird nicht geladen) – zum Öffnen im Browser.</summary>
    public async Task<Uri?> LinkAsync(string attachmentId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "attachments/" + Uri.EscapeDataString(attachmentId));
        request.Options.Set(RoGHttpHandler.SkipEtag, true);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return response.RequestMessage?.RequestUri;
    }

    public async Task<AttachmentContent> LoadAsync(string attachmentId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "attachments/" + Uri.EscapeDataString(attachmentId));
        request.Options.Set(RoGHttpHandler.SkipEtag, true);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxPreviewBytes)
        {
            throw new InvalidDataException("Anhang zu groß für die Vorschau");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.LongLength > MaxPreviewBytes)
        {
            throw new InvalidDataException("Anhang zu groß für die Vorschau");
        }

        return new AttachmentContent(bytes, response.RequestMessage?.RequestUri);
    }
}
