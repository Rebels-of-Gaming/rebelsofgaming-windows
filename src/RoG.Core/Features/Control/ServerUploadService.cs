using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoG.Core.Api;
using RoG.Core.Features.Uploads;

namespace RoG.Core.Features.Control;

/// <summary>Upload-Ordner eines Servers mit erlaubten Endungen, Größenlimit und Freigabe-Pflicht.</summary>
public sealed record UploadTarget(string Directory, IReadOnlyList<string> Extensions, int MaxMb, bool Approval, IReadOnlyList<string> ApprovalExtensions)
{
    public long MaxBytes => MaxMb * 1024L * 1024L;

    /// <summary>Endung (ohne Punkt, klein) erlaubt? Leere Liste = alle.</summary>
    public bool Allows(string fileName) => Extensions.Count == 0 || Extensions.Contains(ServerUploadService.Extension(fileName), StringComparer.OrdinalIgnoreCase);

    /// <summary>Braucht diese Datei eine Vier-Augen-Freigabe (Ordner mit Freigabe oder Code-Endung)?</summary>
    public bool NeedsApproval(string fileName) => Approval || ApprovalExtensions.Contains(ServerUploadService.Extension(fileName), StringComparer.OrdinalIgnoreCase);
}

/// <summary>Person am Upload (hochladend bzw. entscheidend).</summary>
public sealed record UploadPerson(string Id, string Name, string Slug);

/// <summary>
/// Upload auf einen Gameserver. Status: <c>awaiting</c> → <c>verifying</c> → <c>pending</c> (Freigabe) → <c>queued</c> →
/// <c>transferring</c> → <c>completed</c>; sonst <c>failed</c>, <c>rejected</c>, <c>cancelled</c>, <c>expired</c>.
/// <see cref="Entries"/>/<see cref="CodeEntries"/> (Archiv-Inhalt) nur für Freigebende.
/// </summary>
public sealed record ServerUpload(
    string Id,
    string Directory,
    string FileName,
    string Path,
    long Size,
    string Status,
    string? Error,
    bool RequiresApproval,
    string? ApprovalReason,
    int EntriesTotal,
    IReadOnlyList<string> Entries,
    IReadOnlyList<string> CodeEntries,
    bool Overwrite,
    string? Note,
    UploadPerson? Author,
    UploadPerson? DecidedBy,
    string? DecisionNote,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public bool IsOpen => Status is ServerUploadService.Awaiting or "verifying" or ServerUploadService.Pending or "queued" or "transferring";

    public bool IsPending => Status == ServerUploadService.Pending;

    /// <summary>Zurückziehen geht nur, solange noch nichts übertragen wird.</summary>
    public bool IsCancellable => Status is ServerUploadService.Awaiting or "verifying" or ServerUploadService.Pending or "queued";
}

/// <summary>
/// Dateien auf Gameserver hochladen (Website v0.54.0, <c>/v1/servers/{server}/files/uploads</c>): anmelden → Datei per
/// pre-signed <c>PUT</c> in den privaten Zwischenspeicher (nie über die API, ohne Token) → abschließen; die Prüfung und
/// Übertragung zum Server läuft im Hintergrund. Code-Dateien, Code in Archiven und Überschreiben erst nach Freigabe
/// einer zweiten Person (<c>servers.files.approve</c> + 2FA). Rechte: <c>servers.files.upload</c> zum Hochladen.
/// </summary>
public sealed class ServerUploadService(HttpClient api, HttpClient storage)
{
    public const string Awaiting = "awaiting";
    public const string Pending = "pending";
    public const string FilterOpen = "open";
    public const string FilterAll = "all";
    public const int NoteMaxLength = 200;
    public const int FileNameMaxLength = 100;

    public async Task<IReadOnlyList<UploadTarget>> TargetsAsync(string server, CancellationToken cancellationToken = default)
    {
        var data = (await SendAsync(HttpMethod.Get, Base(server) + "/targets", null, cancellationToken).ConfigureAwait(false))?["data"];
        return data?["targets"] is JsonArray list
            ? list.Select(t => Str(t, "directory") is { } dir
                ? new UploadTarget(dir, Strings(t!["extensions"]), Math.Max(1, Int(t, "max_mb")), Bool(t, "approval"), Strings(t["approval_extensions"]))
                : null).OfType<UploadTarget>().ToList()
            : [];
    }

    /// <summary>Uploads des Servers (neueste zuerst, höchstens 100); <paramref name="status"/> <c>open</c>, <c>all</c> oder ein Status.</summary>
    public async Task<IReadOnlyList<ServerUpload>> ListAsync(string server, string status = FilterOpen, CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, Base(server) + "?filter[status]=" + Uri.EscapeDataString(status), null, cancellationToken).ConfigureAwait(false);
        return root?["data"] is JsonArray list ? list.Select(Parse).OfType<ServerUpload>().ToList() : [];
    }

    public async Task<ServerUpload?> GetAsync(string server, string id, CancellationToken cancellationToken = default) =>
        Parse((await SendAsync(HttpMethod.Get, Base(server) + "/" + Id(id), null, cancellationToken).ConfigureAwait(false))?["data"]);

    /// <summary>
    /// Alle drei Schritte: anmelden, Datei in den Speicher laden (Fortschritt 0–1), abschließen. Rückgabe = Upload nach
    /// dem Abschluss (meist <c>verifying</c>); den weiteren Stand per <see cref="GetAsync"/> abfragen.
    /// </summary>
    public async Task<ServerUpload?> UploadAsync(string server, string directory, string fileName, Stream content, long size, bool overwrite = false, string? note = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var body = new JsonObject
        {
            ["directory"] = directory,
            ["filename"] = SafeFileName(fileName),
            ["size"] = size,
        };
        if (overwrite)
        {
            body["overwrite"] = true;
        }

        if (Limit(note, NoteMaxLength) is { } n)
        {
            body["note"] = n;
        }

        var data = (await SendAsync(HttpMethod.Post, Base(server), body, cancellationToken).ConfigureAwait(false))?["data"];
        var upload = Parse(data?["upload"]) ?? throw new ApiProblemException(new ApiProblem { Code = "upload_failed", Status = 0 });
        var url = ApiValues.WebUri(Str(data, "upload_url")) ?? throw new ApiProblemException(new ApiProblem { Code = "upload_failed", Status = 0 });

        using (var request = new HttpRequestMessage(new HttpMethod((Str(data, "method") ?? "PUT").ToUpperInvariant()), url))
        {
            request.Content = new UploadService.ProgressContent(content, size, progress);
            if (data?["headers"] is JsonObject headers)
            {
                foreach (var (name, value) in headers)
                {
                    var text = value is JsonArray a ? a.FirstOrDefault()?.ToString() : value?.ToString();
                    if (text is not null && !request.Headers.TryAddWithoutValidation(name, text))
                    {
                        request.Content.Headers.TryAddWithoutValidation(name, text);
                    }
                }
            }

            using var response = await storage.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new ApiProblemException(new ApiProblem { Code = "upload_failed", Status = (int)response.StatusCode });
            }
        }

        return Parse((await SendAsync(HttpMethod.Post, $"{Base(server)}/{Id(upload.Id)}/complete", new JsonObject(), cancellationToken).ConfigureAwait(false))?["data"]);
    }

    /// <summary>Eigenen Upload zurückziehen (nur solange nicht übertragen wird).</summary>
    public async Task<ServerUpload?> CancelAsync(string server, string id, CancellationToken cancellationToken = default) =>
        Parse((await SendAsync(HttpMethod.Delete, Base(server) + "/" + Id(id), null, cancellationToken).ConfigureAwait(false))?["data"]);

    /// <summary>Freigeben (zweite Person, nie die hochladende; 2FA nötig).</summary>
    public Task<ServerUpload?> ApproveAsync(string server, string id, string? note = null, CancellationToken cancellationToken = default) =>
        DecideAsync(server, id, "approve", note, cancellationToken);

    /// <summary>Ablehnen; die Begründung geht an die hochladende Person.</summary>
    public Task<ServerUpload?> RejectAsync(string server, string id, string? note = null, CancellationToken cancellationToken = default) =>
        DecideAsync(server, id, "reject", note, cancellationToken);

    /// <summary>Endung ohne Punkt, klein (<c>Map.PK3</c> → <c>pk3</c>).</summary>
    public static string Extension(string? fileName)
    {
        var name = SafeFileName(fileName);
        var dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1 ? string.Empty : name[(dot + 1)..].ToLowerInvariant();
    }

    /// <summary>Nur der Dateiname (Windows- und Unix-Pfade), wie die Website bereinigt: erlaubt <c>A–Z a–z 0–9 . _ - +</c>, sonst <c>_</c>.</summary>
    public static string SafeFileName(string? fileName)
    {
        var raw = fileName ?? string.Empty;
        var name = raw[(raw.LastIndexOfAny(['/', '\\']) + 1)..].Trim();
        var chars = name.Select(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-' or '+' ? c : '_').ToArray();
        var clean = new string(chars);
        return clean.Length > FileNameMaxLength ? clean[..FileNameMaxLength] : clean;
    }

    /// <summary>Vorab-Prüfung wie der Server: Fehlercode (<c>extension_not_allowed</c>, <c>file_too_large</c>, <c>invalid_filename</c>) oder <c>null</c>.</summary>
    public static string? Validate(UploadTarget target, string? fileName, long size)
    {
        ArgumentNullException.ThrowIfNull(target);
        var name = SafeFileName(fileName);
        if (name.Length == 0 || name.Trim('.').Length == 0 || Extension(name).Length == 0)
        {
            return "invalid_filename";
        }

        if (!target.Allows(name))
        {
            return "extension_not_allowed";
        }

        return size <= 0 ? "invalid_file" : size > target.MaxBytes ? "file_too_large" : null;
    }

    public static ServerUpload? Parse(JsonNode? n)
    {
        if (Str(n, "id") is not { } id)
        {
            return null;
        }

        var entries = n!["entries"];
        return new ServerUpload(
            id,
            Str(n, "directory") ?? string.Empty,
            Str(n, "filename") ?? string.Empty,
            Str(n, "path") ?? string.Empty,
            n["size"] is JsonValue s && s.TryGetValue<long>(out var size) ? size : 0,
            Str(n, "status") ?? string.Empty,
            Str(n, "error"),
            Bool(n, "requires_approval"),
            Str(n, "approval_reason"),
            Int(entries, "total"),
            Raw(entries?["items"]),
            Raw(entries?["code"]),
            Bool(n, "overwrite"),
            Str(n, "note"),
            Person(n["author"]),
            Person(n["decided_by"]),
            Str(n, "decision_note"),
            ApiValues.Time(Str(n, "created_at")),
            ApiValues.Time(Str(n, "completed_at")));
    }

    private async Task<ServerUpload?> DecideAsync(string server, string id, string action, string? note, CancellationToken cancellationToken)
    {
        var body = new JsonObject();
        if (Limit(note, NoteMaxLength) is { } n)
        {
            body["note"] = n;
        }

        return Parse((await SendAsync(HttpMethod.Post, $"{Base(server)}/{Id(id)}/{action}", body, cancellationToken).ConfigureAwait(false))?["data"]);
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await api.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private static string Base(string server) => "servers/" + Uri.EscapeDataString(server) + "/files/uploads";

    private static string Id(string id) => Uri.EscapeDataString(id);

    private static string? Limit(string? value, int max)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length > max ? t[..max] : t;
    }

    private static UploadPerson? Person(JsonNode? n) =>
        Str(n, "id") is { } id ? new UploadPerson(id, Str(n, "name") ?? string.Empty, Str(n, "slug") ?? string.Empty) : null;

    /// <summary>Archiv-Einträge unverändert (Pfade, Groß/Klein bleibt).</summary>
    private static IReadOnlyList<string> Raw(JsonNode? n) =>
        n is JsonArray a ? a.Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToList() : [];

    /// <summary>Endungen: klein, ohne Punkt; Liste oder kommagetrennt.</summary>
    private static IReadOnlyList<string> Strings(JsonNode? n) => n switch
    {
        JsonArray a => a.Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim().TrimStart('.').ToLowerInvariant() : null).OfType<string>().Where(s => s.Length > 0).ToList(),
        JsonValue v when v.TryGetValue<string>(out var s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.TrimStart('.').ToLowerInvariant()).ToList(),
        _ => [],
    };

    private static string? Str(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    private static int Int(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;

    private static bool Bool(JsonNode? n, string name) =>
        n is JsonObject o && o[name] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}
