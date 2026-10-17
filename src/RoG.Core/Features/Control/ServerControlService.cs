using System.Text.Json;
using System.Text.RegularExpressions;
using RoG.Core.Api;
using RoG.Core.Api.Generated;
using RoG.Core.Realtime;

namespace RoG.Core.Features.Control;

public enum PowerSignal
{
    Start,
    Stop,
    Restart,
    Kill,
}

/// <summary>Was das Konsolen-Token erlaubt (<c>level</c>): lesen, Befehle, Power.</summary>
public enum ConsoleLevel
{
    Read,
    Command,
    Power,
}

public sealed record ConsoleTicket(string Token, Uri Socket, string? Origin, ConsoleLevel Level, bool Demo);

public sealed record ServerResources(string State, double CpuPercent, long Memory, long? MemoryLimit, long Disk, long? DiskLimit, long NetworkRx, long NetworkTx, TimeSpan Uptime)
{
    /// <summary>
    /// Wings-<c>stats</c> (JSON-Text): <c>memory_bytes</c>, <c>memory_limit_bytes</c>, <c>cpu_absolute</c>,
    /// <c>disk_bytes</c>, <c>network.rx_bytes/tx_bytes</c>, <c>state</c>, <c>uptime</c> (ms). <c>null</c> bei Unsinn.
    /// </summary>
    public static ServerResources? FromWingsStats(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            static long L(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? (long)p.GetDouble() : 0;
            var limit = L(r, "memory_limit_bytes");
            var network = r.TryGetProperty("network", out var net) ? net : default;
            return new ServerResources(
                r.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString()! : "unknown",
                r.TryGetProperty("cpu_absolute", out var cpu) && cpu.ValueKind == JsonValueKind.Number ? cpu.GetDouble() : 0,
                L(r, "memory_bytes"),
                limit > 0 ? limit : null,
                L(r, "disk_bytes"),
                null,
                L(network, "rx_bytes"),
                L(network, "tx_bytes"),
                TimeSpan.FromMilliseconds(L(r, "uptime")));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Server-Steuerung über die API (Guide 18): Ressourcen, Power, Befehle, Konsolen-Token. Rechte prüft die API.</summary>
public sealed class ServerControlService(IRoGApiClient client)
{
    public async Task<ServerResources> GetResourcesAsync(string server, CancellationToken cancellationToken = default)
    {
        var d = (await client.V1_servers_resourcesAsync(server, cancellationToken).ConfigureAwait(false)).Data;
        return new ServerResources(d.State, d.Cpu, d.Memory, d.Memory_limit, d.Disk, d.Disk_limit, d.Network_rx, d.Network_tx, TimeSpan.FromSeconds(d.Uptime));
    }

    public async Task PowerAsync(string server, PowerSignal signal, CancellationToken cancellationToken = default) =>
        await client.V1_servers_powerAsync(server, new Body58 { Signal = signal.ToString().ToLowerInvariant() }, cancellationToken).ConfigureAwait(false);

    public async Task SendCommandAsync(string server, string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var line = command.Trim();
        if (line.Contains('\n', StringComparison.Ordinal) || line.Length > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Eine Zeile, höchstens 500 Zeichen");
        }

        await client.V1_servers_commandsAsync(server, new Body59 { Command = line }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConsoleTicket> GetConsoleTicketAsync(string server, CancellationToken cancellationToken = default)
    {
        var d = (await client.V1_servers_consoleTokenAsync(server, cancellationToken).ConfigureAwait(false)).Data;
        var level = JsonSerializer.SerializeToElement(d.Level).GetString() switch
        {
            "power" => ConsoleLevel.Power,
            "command" => ConsoleLevel.Command,
            _ => ConsoleLevel.Read,
        };
        if (!Uri.TryCreate(d.Socket, UriKind.Absolute, out var socket) || (socket.Scheme != "wss" && socket.Scheme != "ws"))
        {
            throw new ApiProblemException(new ApiProblem { Code = "console_unavailable", Status = 0 });
        }

        return new ConsoleTicket(d.Token, socket, ApiValues.Text(d.Origin), level, d.Demo);
    }
}

/// <summary>
/// Live-Konsole über den Wings-WebSocket (Pterodactyl-Protokoll): <c>auth</c> mit kurzlebigem Token, dann
/// <c>console output</c>, <c>status</c>, <c>stats</c>; bei <c>token expiring</c>/<c>token expired</c> wird über
/// <see cref="_renew"/> ein neues Token geholt. Befehle gehen als <c>send command</c>.
/// </summary>
public sealed partial class ConsoleClient(Func<CancellationToken, Task<ConsoleTicket>> renew, Func<string?, IWebSocketConnection> socketFactory) : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<ConsoleTicket>> _renew = renew;
    private readonly CancellationTokenSource _lifetime = new();
    private IWebSocketConnection? _socket;
    private Task? _loop;

    public event EventHandler<string>? Output;

    public event EventHandler<string>? StatusChanged;

    /// <summary>JSON der Wings-Statistik (CPU, Speicher, Netz) – roh, die App formatiert.</summary>
    public event EventHandler<string>? Stats;

    public event EventHandler<Exception?>? Disconnected;

    /// <summary>Wings hat das Token angenommen – ab jetzt kommen Log, Status und Statistik.</summary>
    public event EventHandler? Authenticated;

    /// <summary>Fehler von Wings (<c>daemon error</c>) oder Token dauerhaft abgelehnt (<c>jwt error</c>) – Text für die Anzeige.</summary>
    public event EventHandler<string>? Error;

    public ConsoleLevel Level { get; private set; }

    /// <summary>Demo-Server: es gibt keinen echten Node, also keine Verbindung (die Website zeigt dann Beispielzeilen).</summary>
    public bool IsDemo { get; private set; }

    private int _jwtErrors;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var ticket = await _renew(cancellationToken).ConfigureAwait(false);
        Level = ticket.Level;
        IsDemo = ticket.Demo;
        if (ticket.Demo)
        {
            return;
        }

        _socket = socketFactory(ticket.Origin);
        await _socket.ConnectAsync(ticket.Socket, cancellationToken).ConfigureAwait(false);
        await SendEventAsync("auth", ticket.Token, cancellationToken).ConfigureAwait(false);
        _loop = Task.Run(() => ReceiveLoopAsync(_lifetime.Token), CancellationToken.None);
    }

    /// <summary>Befehl an die Konsole (nur mit <see cref="ConsoleLevel.Command"/> oder höher).</summary>
    public Task SendCommandAsync(string command, CancellationToken cancellationToken = default) =>
        SendEventAsync("send command", command.Trim(), cancellationToken);

    /// <summary>Verlauf beim Verbinden anfordern (Wings schickt die letzten Zeilen als <c>console output</c>).</summary>
    public Task RequestLogsAsync(CancellationToken cancellationToken = default) => SendEventAsync("send logs", null, cancellationToken);

    // Wie die Website: Ereignisse ohne Argument mit `[null]` (Wings erwartet ein Feld)
    private async Task SendEventAsync(string name, string? arg, CancellationToken cancellationToken)
    {
        var socket = _socket ?? throw new InvalidOperationException("Nicht verbunden.");
        var payload = JsonSerializer.Serialize(new { @event = name, args = new[] { arg } });
        await socket.SendAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        Exception? error = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket is { } socket)
            {
                var message = await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                await HandleAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // beendet
        }
#pragma warning disable CA1031 // Netzwerkfehler → als „getrennt“ melden, die Oberfläche bietet Neuverbinden an
        catch (Exception ex)
#pragma warning restore CA1031
        {
            error = ex;
        }

        Disconnected?.Invoke(this, error);
    }

    internal async Task HandleAsync(string message, CancellationToken cancellationToken)
    {
        string? name;
        string? arg;
        try
        {
            using var doc = JsonDocument.Parse(message);
            name = doc.RootElement.TryGetProperty("event", out var e) ? e.GetString() : null;
            arg = doc.RootElement.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0 && a[0].ValueKind == JsonValueKind.String
                ? a[0].GetString()
                : null;
        }
        catch (JsonException)
        {
            return;
        }

        switch (name)
        {
            case "console output" or "install output" or "daemon message" when arg is not null:
                Output?.Invoke(this, StripAnsi(arg));
                break;
            case "daemon error":
                Error?.Invoke(this, StripAnsi(arg ?? name));
                break;
            case "status" when arg is not null:
                StatusChanged?.Invoke(this, arg);
                break;
            case "stats" when arg is not null:
                Stats?.Invoke(this, arg);
                break;
            case "auth success":
                _jwtErrors = 0;
                Authenticated?.Invoke(this, EventArgs.Empty);
                await RequestLogsAsync(cancellationToken).ConfigureAwait(false);
                await SendEventAsync("send stats", null, cancellationToken).ConfigureAwait(false);
                break;
            case "jwt error" when ++_jwtErrors > 2:
                // Token wird immer wieder abgelehnt (z. B. Uhrzeit oder Origin falsch) – nicht endlos neu versuchen
                Error?.Invoke(this, arg ?? name);
                break;
            case "token expiring" or "token expired" or "jwt error":
                var ticket = await _renew(cancellationToken).ConfigureAwait(false);
                Level = ticket.Level;
                await SendEventAsync("auth", ticket.Token, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>ANSI-Farbcodes entfernen (Konsole zeigt reinen Text; Farben kommen später).</summary>
    public static string StripAnsi(string text) => AnsiPattern().Replace(text, string.Empty);

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiPattern();

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (_socket is { } socket)
        {
            try
            {
                await socket.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Schließen darf fehlschlagen
            catch (Exception)
#pragma warning restore CA1031
            {
            }

            await socket.DisposeAsync().ConfigureAwait(false);
        }

        if (_loop is { } loop)
        {
            await Task.WhenAny(loop, Task.Delay(1000)).ConfigureAwait(false);
        }

        _lifetime.Dispose();
    }
}
