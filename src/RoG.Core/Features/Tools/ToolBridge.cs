using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoG.Core.Api;

namespace RoG.Core.Features.Tools;

/// <summary>Was nur das Gerät kann: Zwischenablage, Rückfrage + externer Browser, Höhe.</summary>
public interface IToolBridgeHost
{
    Task CopyAsync(string text);

    /// <summary>Link nach Rückfrage öffnen; <c>false</c> = abgelehnt.</summary>
    Task<bool> OpenLinkAsync(Uri url);

    void Resize(int height);
}

/// <summary>
/// Host der Tool-Bridge für die WebView (Guide 42 „Bridge-Protokoll“, Referenz <c>resources/js/tool-frame.js</c>):
/// nimmt nur <c>{rog:"bridge", v:1}</c> an, prüft Methode, Größe (128 KB) und Textlängen, höchstens 8 Anfragen
/// gleichzeitig (Dateien: 4, Warteschlange 250), <c>copy</c> 1× je Sekunde, <c>openLink</c> nur http(s) mit
/// Rückfrage, 1 je Sekunde und 10 je Minute, nach 5 Fehlversuchen 60 s gesperrt. Antworten als JSON-Text für
/// <c>CoreWebView2.PostWebMessageAsJson</c>.
/// </summary>
public sealed class ToolBridge(IToolBridgeApi api, IToolBridgeHost host, string slug, TimeProvider? time = null) : IDisposable
{
    public const int MaxMessageBytes = 131072;
    public const int MaxInFlight = 8;
    public const long AssetMaxBytes = 3 * 1024 * 1024;
    public const int AssetConcurrency = 4;
    public const int AssetMaxQueue = 250;

    private static readonly string[] Methods = ["me", "squad", "data.get", "data.set", "data.delete", "data.list", "data.listMembers", "copy", "assets.list", "assets.get", "openLink"];
    private static readonly string[] Scopes = ["personal", "squad", "global"];
    private static readonly (string Name, int Max)[] MaxLength = [("key", 64), ("scope", 10), ("prefix", 64), ("text", 10000), ("name", 80), ("url", 2048)];

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _assetSlots = new(AssetConcurrency, AssetConcurrency);
    private readonly List<DateTimeOffset> _links = [];
    private readonly List<DateTimeOffset> _linkFailures = [];
    private int _inFlight;
    private int _assetQueue;
    private DateTimeOffset _lastCopy = DateTimeOffset.MinValue;
    private DateTimeOffset _linkBlockedUntil = DateTimeOffset.MinValue;
    private Task<IReadOnlyList<ToolAsset>>? _assets;

    /// <summary>Die App hat nur ein dunkles Design.</summary>
    public string Theme { get; init; } = "dark";

    public void Dispose() => _assetSlots.Dispose();

    public static string ThemeMessage(string theme) =>
        new JsonObject { ["rog"] = "host", ["v"] = 1, ["type"] = "theme", ["theme"] = theme == "light" ? "light" : "dark" }.ToJsonString();

    /// <summary>Nachricht des Tools verarbeiten; Rückgabe = Antwort (JSON) oder <c>null</c> (Mitteilung/ungültig).</summary>
    public async Task<string?> HandleAsync(string json, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        var tooLarge = Encoding.UTF8.GetByteCount(json) > MaxMessageBytes;
        JsonObject? message;
        try
        {
            message = tooLarge ? null : JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        if (tooLarge)
        {
            // zu groß zum Parsen – nur die ID suchen, um ehrlich mit value_too_large zu antworten
            return LargeMessageId(json) is { } largeId ? Failure(largeId, "value_too_large", "Message too large.") : null;
        }

        if (message is null || Str(message, "rog") != "bridge" || !IsVersionOne(message["v"]) || Str(message, "method") is not { } method)
        {
            return null;
        }

        var parameters = message["params"] as JsonObject ?? [];
        switch (method)
        {
            case "resize":
                if (parameters["height"] is JsonValue h && h.TryGetValue<double>(out var height) && double.IsFinite(height))
                {
                    host.Resize((int)Math.Clamp(Math.Ceiling(height), 80, 8000));
                }

                return null;
            case "ready":
                return ThemeMessage(Theme);
        }

        if (Str(message, "id") is not { Length: <= 40 } id)
        {
            return null;
        }

        if (BadParams(parameters))
        {
            return Failure(id, "value_too_large", "Message too large or invalid parameters.");
        }

        if (method == "assets.get")
        {
            return await AssetAsync(id, Str(parameters, "name") ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }

        lock (_gate)
        {
            if (_inFlight >= MaxInFlight)
            {
                return Failure(id, "rate_limited", "Too many parallel requests.");
            }

            _inFlight++;
        }

        try
        {
            return Success(id, await DispatchAsync(method, parameters, cancellationToken).ConfigureAwait(false));
        }
        catch (ToolBridgeException ex)
        {
            return Failure(id, ex.Code, ex.Message);
        }
        catch (ApiProblemException ex)
        {
            return Failure(id, ex.Problem.Code, ex.Problem.Detail ?? ex.Problem.Title ?? ex.Problem.Code);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or TaskCanceledException)
        {
            return Failure(id, "host_error", "The request failed.");
        }
        finally
        {
            lock (_gate)
            {
                _inFlight--;
            }
        }
    }

    private async Task<JsonNode?> DispatchAsync(string method, JsonObject p, CancellationToken ct)
    {
        if (!Methods.Contains(method))
        {
            throw new ToolBridgeException("validation_failed", "Unknown method.");
        }

        switch (method)
        {
            case "me":
                return (await api.ShowAsync(slug, ct).ConfigureAwait(false))?["viewer"]?.DeepClone();
            case "squad":
                return await api.SquadAsync(slug, ct).ConfigureAwait(false);
            case "data.get":
                return (await api.DataGetAsync(slug, Scope(p), Key(p), ct).ConfigureAwait(false))?["value"]?.DeepClone();
            case "data.set":
                return await api.DataSetAsync(slug, Scope(p), Key(p), p["value"], ct).ConfigureAwait(false);
            case "data.delete":
                await api.DataDeleteAsync(slug, Scope(p), Key(p), ct).ConfigureAwait(false);
                return null;
            case "data.list":
                return ListResult(await api.DataListAsync(slug, Scope(p), Prefix(p), ct).ConfigureAwait(false));
            case "data.listMembers":
                return ListResult(await api.MembersAsync(slug, Key(p), ct).ConfigureAwait(false));
            case "assets.list":
                return new JsonArray((await AssetListAsync(ct).ConfigureAwait(false))
                    .Select(a => (JsonNode)new JsonObject { ["name"] = a.Name, ["mime"] = a.Mime, ["size"] = a.Size, ["width"] = a.Width, ["height"] = a.Height })
                    .ToArray());
            case "copy":
                return await CopyAsync(Str(p, "text") ?? string.Empty).ConfigureAwait(false);
            case "openLink":
                return await OpenLinkAsync(Str(p, "url")).ConfigureAwait(false);
            default:
                throw new ToolBridgeException("validation_failed", "Unknown method.");
        }
    }

    private async Task<JsonNode?> CopyAsync(string text)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (now - _lastCopy < TimeSpan.FromSeconds(1))
            {
                throw new ToolBridgeException("rate_limited", "Copy at most once per second.");
            }

            _lastCopy = now;
        }

        try
        {
            await host.CopyAsync(text).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not ToolBridgeException)
        {
            throw new ToolBridgeException("copy_failed", "Copy failed.");
        }

        return null;
    }

    private async Task<JsonNode?> OpenLinkAsync(string? raw)
    {
        Uri url;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (now < _linkBlockedUntil)
            {
                throw new ToolBridgeException("rate_limited", "Links are blocked for a while.");
            }

            try
            {
                if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https") || parsed.UserInfo.Length > 0)
                {
                    throw new ToolBridgeException("validation_failed", "Only http(s) links.");
                }

                _links.RemoveAll(t => now - t >= TimeSpan.FromMinutes(1));
                if (_links.Count >= 10 || (_links.Count > 0 && now - _links[^1] < TimeSpan.FromSeconds(1)))
                {
                    throw new ToolBridgeException("rate_limited", "Too many links.");
                }

                _links.Add(now);
                url = parsed;
            }
            catch (ToolBridgeException)
            {
                // auch Fehlversuche zählen: 5 in einer Minute → 60 s gesperrt (wie die Website)
                _linkFailures.RemoveAll(t => now - t >= TimeSpan.FromMinutes(1));
                _linkFailures.Add(now);
                if (_linkFailures.Count >= 5)
                {
                    _linkBlockedUntil = now + TimeSpan.FromSeconds(60);
                    _linkFailures.Clear();
                }

                throw;
            }
        }

        // statt Nutzer-Aktivierung (gibt es in der WebView nicht) fragt die App nach – mit Ziel-Host
        return await host.OpenLinkAsync(url).ConfigureAwait(false)
            ? null
            : throw new ToolBridgeException("cancelled", "The link was not opened.");
    }

    private async Task<string> AssetAsync(string id, string name, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_assetQueue >= AssetMaxQueue)
            {
                return Failure(id, "rate_limited", "Too many asset requests.");
            }

            _assetQueue++;
        }

        try
        {
            await _assetSlots.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // nur Dateien dieses Tools (Liste vom Server)
                var item = (await AssetListAsync(ct).ConfigureAwait(false)).FirstOrDefault(a => a.Name == name)
                    ?? throw new ToolBridgeException("not_found", "Unknown asset.");
                if (item.Size > AssetMaxBytes)
                {
                    throw new ToolBridgeException("value_too_large", "Asset too large.");
                }

                var content = await api.AssetAsync(slug, item.Name, AssetMaxBytes, ct).ConfigureAwait(false);
                if (!string.Equals(content.Mime, item.Mime, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ToolBridgeException("host_error", "Unexpected asset type.");
                }

                return Success(id, new JsonObject
                {
                    ["name"] = item.Name,
                    ["mime"] = item.Mime,
                    ["size"] = content.Bytes.Length,
                    ["base64"] = Convert.ToBase64String(content.Bytes),
                });
            }
            finally
            {
                _assetSlots.Release();
            }
        }
        catch (ToolBridgeException ex)
        {
            return Failure(id, ex.Code, ex.Message);
        }
        catch (ApiProblemException ex)
        {
            return Failure(id, ex.Problem.Status == 404 ? "not_found" : ex.Problem.Code, "The asset could not be loaded.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or TaskCanceledException)
        {
            return Failure(id, "host_error", "The asset could not be loaded.");
        }
        finally
        {
            lock (_gate)
            {
                _assetQueue--;
            }
        }
    }

    private Task<IReadOnlyList<ToolAsset>> AssetListAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            // einmal je geladenem Tool; ein Fehler wird beim nächsten Aufruf neu versucht
            if (_assets is null || _assets.IsFaulted || _assets.IsCanceled)
            {
                _assets = api.AssetsAsync(slug, ct);
            }

            return _assets;
        }
    }

    private static JsonObject ListResult(ToolDataPage page) => new() { ["items"] = page.Items?.DeepClone() ?? new JsonArray(), ["truncated"] = page.Truncated };

    private static string Scope(JsonObject p)
    {
        var scope = Str(p, "scope") ?? "personal";
        return Scopes.Contains(scope) ? scope : throw new ToolBridgeException("validation_failed", "Invalid scope.");
    }

    private static string Key(JsonObject p) =>
        Str(p, "key") is { Length: > 0 } key && key.Trim('.').Length > 0 ? key : throw new ToolBridgeException("validation_failed", "Invalid key.");

    private static string? Prefix(JsonObject p) => Str(p, "prefix") is { Length: > 0 } prefix ? prefix : null;

    private static bool BadParams(JsonObject p) =>
        MaxLength.Any(m => p[m.Name] is { } v && (v.GetValueKind() != JsonValueKind.String || v.GetValue<string>().Length > m.Max));

    private static bool IsVersionOne(JsonNode? v) => v is JsonValue value && value.TryGetValue<double>(out var d) && d == 1;

    private static string? Str(JsonObject o, string name) =>
        o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    private static string? LargeMessageId(string json)
    {
        var match = System.Text.RegularExpressions.Regex.Match(json, "\"id\"\\s*:\\s*\"([^\"\\\\]{1,40})\"", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100));
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string Success(string id, JsonNode? data) =>
        new JsonObject { ["rog"] = "host", ["v"] = 1, ["type"] = "response", ["id"] = id, ["ok"] = true, ["data"] = data }.ToJsonString();

    private static string Failure(string id, string code, string message) =>
        new JsonObject
        {
            ["rog"] = "host", ["v"] = 1, ["type"] = "response", ["id"] = id, ["ok"] = false,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        }.ToJsonString();
}
