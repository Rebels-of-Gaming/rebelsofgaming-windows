using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using RoG.Core.Api;

namespace RoG.Core.Features.Minecraft;

/// <summary>
/// Java-Ressourcenpaket des Netzwerks (Website v0.44.0, Annahme 501): <see cref="Url"/> versioniert (unveränderlich),
/// <see cref="LatestUrl"/> fester Alias (kann fehlen), <see cref="Sha1"/> = Wert für <c>resource-pack-sha1</c>.
/// </summary>
public sealed record MinecraftResourcePack(Uri Url, Uri? LatestUrl, string Sha1, long Size, DateTimeOffset? UpdatedAt);

public sealed partial class MinecraftService
{
    public const string ResourcePackNotFound = "resource_pack_not_found";

    /// <summary>Höchstens 50 MB (Prüfung der Website; die App prüft vorab, um den Upload nicht umsonst zu senden).</summary>
    public const long ResourcePackMaxBytes = 50L * 1024 * 1024;

    /// <summary>Aktuelles Paket (öffentlich, mit Token <c>servers:read</c>); ohne Paket <c>null</c>.</summary>
    public async Task<MinecraftResourcePack?> ResourcePackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return ParseResourcePack((await SendAsync(HttpMethod.Get, "minecraft/resource-pack", null, cancellationToken).ConfigureAwait(false))?["data"]);
        }
        catch (ApiProblemException ex) when (ex.Problem.Code == ResourcePackNotFound)
        {
            return null;
        }
    }

    /// <summary>Neues Paket hochladen (<c>minecraft:manage</c> + <c>minecraft.manage</c>, ZIP mit <c>pack.mcmeta</c>, ≤ 50 MB).</summary>
    public async Task<MinecraftResourcePack?> UploadResourcePackAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!IsResourcePackName(fileName))
        {
            throw new ArgumentException("Only .zip files.", nameof(fileName));
        }

        using var form = new MultipartFormDataContent();
        var part = new StreamContent(content);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        // nur der Dateiname, auch bei Windows-Pfaden unter Linux (Tests)
        form.Add(part, "file", fileName[(fileName.LastIndexOfAny(['\\', '/']) + 1)..]);
        using var request = new HttpRequestMessage(HttpMethod.Post, "minecraft/resource-pack") { Content = form };
        request.Options.Set(RoGHttpHandler.SkipEtag, true);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : ParseResourcePack(JsonNode.Parse(text)?["data"]);
    }

    /// <summary>Paket löschen; der feste Alias verschwindet sofort.</summary>
    public async Task DeleteResourcePackAsync(CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Delete, "minecraft/resource-pack", null, cancellationToken).ConfigureAwait(false);

    /// <summary>Nur <c>.zip</c> (Groß-/Kleinschreibung egal).</summary>
    public static bool IsResourcePackName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && Path.GetExtension(fileName).Equals(".zip", StringComparison.OrdinalIgnoreCase);

    internal static MinecraftResourcePack? ParseResourcePack(JsonNode? n)
    {
        if (HttpsUri(Str(n, "url")) is not { } url || Str(n, "sha1") is not { } sha1 || sha1.Length != 40 || !sha1.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        return new MinecraftResourcePack(url, HttpsUri(Str(n, "latest_url")), sha1.ToLowerInvariant(), Math.Max(0, LongOf(n, "size")), Time(n, "updated_at"));
    }

    private static Uri? HttpsUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;
}
