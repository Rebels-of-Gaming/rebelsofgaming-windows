using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Control;

/// <summary>Datei auf der Config-Whitelist eines Servers.</summary>
public sealed record ConfigFile(string Id, string Path, string? Label, string Syntax, string? ReloadCommand, bool RequiresApproval, string? Template = null)
{
    public bool HasTemplate => !string.IsNullOrEmpty(Template);

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Path : $"{Label} ({Path})";
}

/// <summary>Zeile im Diff: <c>add</c>, <c>remove</c> oder <c>context</c>.</summary>
public sealed record DiffLine(string Type, string Text, int? Old, int? New)
{
    public string Display => (Type switch { "add" => "+ ", "remove" or "del" or "delete" => "− ", _ => "  " }) + Text;
}

public sealed record ConfigPreview(IReadOnlyList<DiffLine> Diff, IReadOnlyList<string> Errors, bool Changed);

public sealed record ConfigVersion(string Id, string? Note, string? Author, DateTimeOffset? CreatedAt);

/// <summary>Was nach dem Speichern passiert: nur speichern, Reload-Befehl ausführen oder Server neu starten.</summary>
public enum ConfigAfter
{
    Save,
    Exec,
    Restart,
}

public enum ConfigSaveOutcome
{
    Saved,

    /// <summary>Vier-Augen-Pflicht: Änderung ist als Antrag eingereicht (<c>202</c>).</summary>
    Pending,
}

/// <summary>
/// Config-Editor (Guide 18 „Configs“): Whitelist, Inhalt lesen, prüfen (Diff + Validierung), speichern, Versionen,
/// Rollback. Speichern läuft über <see cref="HttpClient"/>, weil <c>202</c> (Antrag) eine andere Form hat als die Spec.
/// </summary>
public sealed class ServerConfigService(IRoGApiClient client, HttpClient http)
{
    public async Task<IReadOnlyList<ConfigFile>> ListAsync(string server, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_servers_configs_indexAsync(server, cancellationToken).ConfigureAwait(false);
        return response.Data
            .Select(f => new ConfigFile(f.Id, f.Path, ApiValues.Text(f.Label), f.Syntax, ApiValues.Text(f.Reload_command), f.Requires_approval, ApiValues.Text(f.Template)))
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string> ReadAsync(string server, string path, CancellationToken cancellationToken = default) =>
        (await client.V1_servers_files_contentAsync(server, path, cancellationToken).ConfigureAwait(false)).Data.Content;

    public async Task<ConfigPreview> PreviewAsync(string server, string fileId, string content, CancellationToken cancellationToken = default)
    {
        var d = (await client.V1_servers_configs_previewAsync(server, fileId, new Body50 { Content = content }, cancellationToken).ConfigureAwait(false)).Data;
        return new ConfigPreview(
            d.Diff?.Select(l => new DiffLine(l.Type, l.Text, l.Old, l.New)).ToList() ?? [],
            d.Errors?.ToList() ?? [],
            d.Changed);
    }

    public async Task<ConfigSaveOutcome> SaveAsync(string server, string fileId, string content, ConfigAfter after, string? note, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["content"] = content,
            ["after"] = after.ToString().ToLowerInvariant(),
        };
        if (ApiValues.Text(note) is { } n)
        {
            body["note"] = n;
        }

        using var response = await http.PutAsJsonAsync(
            new Uri($"servers/{Uri.EscapeDataString(server)}/configs/{Uri.EscapeDataString(fileId)}", UriKind.Relative), body, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode(); // Fehler (RFC 9457) wirft schon die Pipeline
        return response.StatusCode == HttpStatusCode.Accepted ? ConfigSaveOutcome.Pending : ConfigSaveOutcome.Saved;
    }

    public async Task<IReadOnlyList<ConfigVersion>> VersionsAsync(string server, string fileId, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_servers_configs_versionsAsync(server, fileId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Data
            .Select(v => new ConfigVersion(v.Id, ApiValues.Text(v.Note), v.User?.Name, ApiValues.Time(v.Created_at)))
            .OrderByDescending(v => v.CreatedAt)
            .ToList();
    }

    public async Task RollbackAsync(string server, string versionId, ConfigAfter after, CancellationToken cancellationToken = default) =>
        await client.V1_servers_configs_rollbackAsync(server, versionId, new Body52 { After = after.ToString().ToLowerInvariant() }, cancellationToken).ConfigureAwait(false);
}

public sealed record ServerBackup(string Uuid, string Name, long Bytes, DateTimeOffset? CreatedAt, bool Completed, bool Successful, bool Locked);

/// <summary>Backups (Guide 18 „Backups“): Liste, anlegen (läuft im Hintergrund), Download-Link, wiederherstellen, sperren, löschen.</summary>
public sealed class ServerBackupService(IRoGApiClient client)
{
    public async Task<IReadOnlyList<ServerBackup>> ListAsync(string server, CancellationToken cancellationToken = default)
    {
        var response = await client.V1_servers_backups_indexAsync(server, cancellationToken).ConfigureAwait(false);
        return response.Data
            .Select(b => new ServerBackup(b.Uuid, b.Name, b.Bytes, ApiValues.Time(b.Created_at), b.Completed_at is not null, b.Successful, b.Locked))
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    public async Task CreateAsync(string server, string? name, CancellationToken cancellationToken = default) =>
        await client.V1_servers_backups_storeAsync(server, ApiValues.Text(name) is { } n ? new Body45 { Name = n } : null, cancellationToken).ConfigureAwait(false);

    /// <summary>Signierte, kurzlebige Adresse direkt vom Node (ohne Token – im Browser öffnen).</summary>
    public async Task<Uri> DownloadUrlAsync(string server, string uuid, CancellationToken cancellationToken = default)
    {
        var url = (await client.V1_servers_backups_downloadAsync(server, uuid, cancellationToken).ConfigureAwait(false)).Data.Url;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : throw new ApiProblemException(new ApiProblem { Code = "download_failed", Status = 0 });
    }

    public async Task RestoreAsync(string server, string uuid, bool truncate, CancellationToken cancellationToken = default) =>
        await client.V1_servers_backups_restoreAsync(server, uuid, new Body46 { Truncate = truncate }, cancellationToken).ConfigureAwait(false);

    public async Task SetLockedAsync(string server, string uuid, bool locked, CancellationToken cancellationToken = default) =>
        await client.V1_servers_backups_lockAsync(server, uuid, new Body47 { Locked = locked }, cancellationToken).ConfigureAwait(false);

    public async Task DeleteAsync(string server, string uuid, CancellationToken cancellationToken = default) =>
        await client.V1_servers_backups_destroyAsync(server, uuid, cancellationToken).ConfigureAwait(false);
}
