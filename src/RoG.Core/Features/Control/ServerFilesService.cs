using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Control;

/// <summary>Eintrag im Dateibrowser; <see cref="Operable"/> = kopierbar, <see cref="IsBackup"/> = umbenenn-/löschbar.</summary>
public sealed record ServerFileEntry(string Name, string Path, bool IsFile, long Size, DateTimeOffset? ModifiedAt, bool Editable, bool Operable, bool IsBackup)
{
    public string SizeText => IsFile ? ServerPaths.FormatSize(Size) : string.Empty;
}

/// <summary>Pfad-Helfer für den Dateibrowser (Pfade relativ zum Server-Root, Trenner <c>/</c>).</summary>
public static class ServerPaths
{
    public static string Normalize(string? path) =>
        string.Join('/', (path ?? string.Empty).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));

    public static string Parent(string? path)
    {
        var p = Normalize(path);
        var i = p.LastIndexOf('/');
        return i < 0 ? string.Empty : p[..i];
    }

    public static string Combine(string? directory, string name)
    {
        var d = Normalize(directory);
        return d.Length == 0 ? Normalize(name) : $"{d}/{Normalize(name)}";
    }

    /// <summary>Brotkrumen: (Anzeige, Pfad) vom Root bis zum Ordner.</summary>
    public static IReadOnlyList<(string Name, string Path)> Crumbs(string? directory)
    {
        var parts = Normalize(directory).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var list = new List<(string, string)> { ("/", string.Empty) };
        for (var i = 0; i < parts.Length; i++)
        {
            list.Add((parts[i], string.Join('/', parts[..(i + 1)])));
        }

        return list;
    }

    /// <summary>Sicherungen enden auf <c>.bak</c> oder <c>.old</c> (Guide 18) – nur die darf man umbenennen/löschen.</summary>
    public static bool IsBackupName(string? path) =>
        path is not null && (path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".old", StringComparison.OrdinalIgnoreCase));

    /// <summary>Vorschlag für eine Sicherungskopie, der keinen vorhandenen Namen trifft: <c>x.cfg.bak</c>, <c>x.cfg.2.bak</c> …</summary>
    public static string SuggestBackup(string path, IEnumerable<string> existing)
    {
        var taken = new HashSet<string>(existing.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        var p = Normalize(path);
        var candidate = $"{p}.bak";
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{p}.{n.ToString(CultureInfo.InvariantCulture)}.bak";
        }

        return candidate;
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1048576 => (bytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture) + " KB",
        _ => (bytes / 1048576.0).ToString("0.#", CultureInfo.CurrentCulture) + " MB",
    };
}

/// <summary>
/// Dateien eines Servers (Guide 18 „Configs“): Ordner lesen, Textdatei lesen, Sicherungen kopieren/umbenennen/löschen.
/// Die Operationen haben in der Spec keinen Body (Lücke W13) – die App sendet <c>{path, to}</c> selbst.
/// </summary>
public sealed class ServerFilesService(IRoGApiClient client, HttpClient http)
{
    public async Task<IReadOnlyList<ServerFileEntry>> ListAsync(string server, string? directory, CancellationToken cancellationToken = default)
    {
        var dir = ServerPaths.Normalize(directory);
        var response = await client.V1_servers_files_indexAsync(server, dir.Length == 0 ? null : dir, cancellationToken).ConfigureAwait(false);
        return Sort(response.Data.Select(f => new ServerFileEntry(
            f.Name,
            ServerPaths.Normalize(string.IsNullOrEmpty(f.Path) ? ServerPaths.Combine(dir, f.Name) : f.Path),
            f.Is_file,
            f.Size,
            ApiValues.Time(f.Modified_at),
            f.Editable,
            f.Operable,
            f.Backup)));
    }

    /// <summary>Ordner zuerst, dann Dateien – jeweils nach Name.</summary>
    public static IReadOnlyList<ServerFileEntry> Sort(IEnumerable<ServerFileEntry> entries) =>
        entries.OrderBy(e => e.IsFile).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public async Task<string> ReadAsync(string server, string path, CancellationToken cancellationToken = default) =>
        (await client.V1_servers_files_contentAsync(server, ServerPaths.Normalize(path), cancellationToken).ConfigureAwait(false)).Data.Content;

    /// <summary>Kopie als Sicherung; das Ziel muss auf <c>.bak</c>/<c>.old</c> enden. Antwort: tatsächlicher Pfad.</summary>
    public Task<string> CopyAsync(string server, string path, string to, CancellationToken cancellationToken = default) =>
        OperateAsync(server, "copy", path, to, cancellationToken);

    public Task<string> RenameAsync(string server, string path, string to, CancellationToken cancellationToken = default) =>
        OperateAsync(server, "rename", path, to, cancellationToken);

    public async Task DeleteAsync(string server, string path, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            Endpoint(server, "delete"), new Dictionary<string, string> { ["path"] = ServerPaths.Normalize(path) }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> OperateAsync(string server, string operation, string path, string to, CancellationToken cancellationToken)
    {
        var target = ServerPaths.Normalize(to);
        if (!ServerPaths.IsBackupName(target))
        {
            throw new ArgumentException("Ziel muss auf .bak oder .old enden", nameof(to));
        }

        using var response = await http.PostAsJsonAsync(
            Endpoint(server, operation), new Dictionary<string, string> { ["path"] = ServerPaths.Normalize(path), ["to"] = target }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("path", out var p) && p.GetString() is { } s ? s : target;
    }

    private static Uri Endpoint(string server, string operation) =>
        new($"servers/{Uri.EscapeDataString(server)}/files/{operation}", UriKind.Relative);
}

/// <summary>Eintrag der Server-Whitelist (Zeile vom Server, erkanntes Mitglied).</summary>
public sealed record WhitelistEntry(string Line, string? MemberId, string? MemberName)
{
    public string Display => MemberName is { } n ? $"{n} ({Line})" : Line;
}

/// <summary>Whitelist (Guide 18, Recht <c>servers.bans</c>): lesen, Mitglied oder Spielername hinzufügen/entfernen.</summary>
public sealed class ServerWhitelistService(IRoGApiClient client, HttpClient http)
{
    public async Task<IReadOnlyList<WhitelistEntry>> ListAsync(string server, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_servers_whitelist_indexAsync(server, cancellationToken).ConfigureAwait(false);
        return response.Data
            .Select(e => new WhitelistEntry(e.Line, e.Member?.Id, e.Member?.Name))
            .OrderBy(e => e.MemberName ?? e.Line, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task AddAsync(string server, string? userId, string? playerName, CancellationToken cancellationToken = default) =>
        UpdateAsync(server, "add", userId, playerName, cancellationToken);

    public Task RemoveAsync(string server, string? userId, string? playerName, CancellationToken cancellationToken = default) =>
        UpdateAsync(server, "remove", userId, playerName, cancellationToken);

    private async Task UpdateAsync(string server, string action, string? userId, string? playerName, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, string> { ["action"] = action };
        if (ApiValues.Text(userId) is { } id)
        {
            body["user_id"] = id;
        }
        else if (ApiValues.Text(playerName) is { } name)
        {
            body["player_name"] = name;
        }
        else
        {
            throw new ArgumentException("Mitglied oder Spielername nötig", nameof(playerName));
        }

        using var response = await http.PostAsJsonAsync(
            new Uri($"servers/{Uri.EscapeDataString(server)}/whitelist", UriKind.Relative), body, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Feld einer Formular-Vorlage: <c>text</c>, <c>password</c>, <c>number</c>, <c>bool</c>, <c>select</c>, <c>list</c>.</summary>
public sealed record TemplateField(string Name, string Type, string Label, int? Min, int? Max, IReadOnlyList<string> Options);

/// <summary>Formular einer Config-Datei mit aktuellen Werten (Passwörter nie – nur <see cref="PasswordSet"/>).</summary>
public sealed record ConfigTemplateForm(
    string Key,
    string Label,
    string? Description,
    IReadOnlyList<TemplateField> Fields,
    IReadOnlyDictionary<string, JsonElement> Values,
    IReadOnlySet<string> PasswordSet)
{
    /// <summary>Aktueller Wert als Text für ein Eingabefeld (Listen: eine Zeile je Eintrag).</summary>
    public string TextOf(string field)
    {
        if (!Values.TryGetValue(field, out var v))
        {
            return string.Empty;
        }

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? string.Empty,
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => string.Join("\n", v.EnumerateArray().Select(i => i.ValueKind == JsonValueKind.String ? i.GetString() : i.GetRawText())),
            _ => string.Empty,
        };
    }

    public bool BoolOf(string field) =>
        Values.TryGetValue(field, out var v) && (v.ValueKind == JsonValueKind.True
            || (v.ValueKind == JsonValueKind.String && v.GetString() is "true" or "1" or "on")
            || (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n != 0));
}

/// <summary>Eingabe je Feld aus dem Formular; <see cref="ClearPassword"/> entfernt ein gesetztes Passwort.</summary>
public sealed record TemplateInput(string Text, bool Checked = false, bool ClearPassword = false);

/// <summary>Formular-Vorlagen (Guide 18): lesen, Werte prüfen und umwandeln, anwenden (wie Speichern, ggf. Antrag).</summary>
public sealed partial class ConfigTemplateService(HttpClient http)
{
    public async Task<ConfigTemplateForm> GetAsync(string server, string fileId, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(Endpoint(server, fileId), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Parse(doc.RootElement);
    }

    public async Task<ConfigSaveOutcome> ApplyAsync(
        string server, string fileId, IReadOnlyDictionary<string, object?> values, ConfigAfter after, string? note, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["values"] = values,
            ["after"] = after.ToString().ToLowerInvariant(),
        };
        if (ApiValues.Text(note) is { } n)
        {
            body["note"] = n;
        }

        using var response = await http.PutAsJsonAsync(Endpoint(server, fileId), body, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return response.StatusCode == HttpStatusCode.Accepted ? ConfigSaveOutcome.Pending : ConfigSaveOutcome.Saved;
    }

    internal static ConfigTemplateForm Parse(JsonElement root)
    {
        var data = root.TryGetProperty("data", out var d) ? d : root;
        var template = data.TryGetProperty("template", out var t) ? t : default;
        static string? S(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        static int? I(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i) ? i : null;

        var fields = new List<TemplateField>();
        if (template.ValueKind == JsonValueKind.Object && template.TryGetProperty("fields", out var fs) && fs.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in fs.EnumerateArray())
            {
                var name = S(f, "name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var options = f.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array
                    ? o.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToList()
                    : [];
                fields.Add(new TemplateField(name, S(f, "type") ?? "text", S(f, "label") ?? name, I(f, "min"), I(f, "max"), options));
            }
        }

        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (data.TryGetProperty("values", out var vs) && vs.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in vs.EnumerateObject())
            {
                values[p.Name] = p.Value.Clone();
            }
        }

        var passwordSet = new HashSet<string>(StringComparer.Ordinal);
        if (data.TryGetProperty("password_set", out var ps) && ps.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in ps.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.True))
            {
                passwordSet.Add(p.Name);
            }
        }

        return new ConfigTemplateForm(S(template, "key") ?? string.Empty, S(template, "label") ?? string.Empty, ApiValues.Text(S(template, "description")), fields, values, passwordSet);
    }

    /// <summary>
    /// Wandelt die Eingaben in <c>values</c> um und prüft sie wie die Website (Bereich, Auswahl, keine <c>" ; \</c>,
    /// Map-Namen). Leere Passwörter bleiben unverändert (nicht gesendet), <see cref="TemplateInput.ClearPassword"/> sendet <c>""</c>.
    /// Fehler: Feldnamen.
    /// </summary>
    public static (IReadOnlyDictionary<string, object?> Values, IReadOnlyList<string> Errors) Build(
        IReadOnlyList<TemplateField> fields, IReadOnlyDictionary<string, TemplateInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(inputs);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var field in fields)
        {
            if (!inputs.TryGetValue(field.Name, out var input))
            {
                continue;
            }

            var text = input.Text?.Trim() ?? string.Empty;
            switch (field.Type)
            {
                case "bool":
                    values[field.Name] = input.Checked;
                    break;
                case "number":
                    if (text.Length == 0)
                    {
                        break;
                    }

                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                        && n >= (field.Min ?? 0) && n <= (field.Max ?? 65535))
                    {
                        values[field.Name] = n;
                    }
                    else
                    {
                        errors.Add(field.Name);
                    }

                    break;
                case "select":
                    if (text.Length == 0)
                    {
                        break;
                    }

                    if (field.Options.Contains(text, StringComparer.Ordinal))
                    {
                        values[field.Name] = text;
                    }
                    else
                    {
                        errors.Add(field.Name);
                    }

                    break;
                case "list":
                    var items = (input.Text ?? string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                    if (items.Count == 0 || items.Count > (field.Max ?? 50) || items.Exists(i => !MapName().IsMatch(i)))
                    {
                        errors.Add(field.Name);
                    }
                    else
                    {
                        values[field.Name] = items;
                    }

                    break;
                case "password":
                    if (input.ClearPassword)
                    {
                        values[field.Name] = string.Empty;
                    }
                    else if (text.Length > 0)
                    {
                        if (IsSafe(text, field.Max))
                        {
                            values[field.Name] = text;
                        }
                        else
                        {
                            errors.Add(field.Name);
                        }
                    }

                    break;
                default:
                    if (IsSafe(text, field.Max))
                    {
                        values[field.Name] = text;
                    }
                    else
                    {
                        errors.Add(field.Name);
                    }

                    break;
            }
        }

        return (values, errors);
    }

    private static bool IsSafe(string text, int? max) =>
        text.Length <= (max ?? 128) && !text.Any(c => c is '"' or ';' or '\\' || char.IsControl(c));

    [GeneratedRegex(@"^[A-Za-z0-9_\-./]{1,64}$")]
    private static partial Regex MapName();

    private static Uri Endpoint(string server, string fileId) =>
        new($"servers/{Uri.EscapeDataString(server)}/configs/{Uri.EscapeDataString(fileId)}/template", UriKind.Relative);
}
