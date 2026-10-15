using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RoG.Core.Auth;

/// <summary>Ergebnis des Browser-Rücksprungs an <c>http://127.0.0.1:&lt;Port&gt;/callback</c>.</summary>
public sealed record LoopbackCallback(string? Code, string? State, string? Error, string? ErrorDescription)
{
    public bool IsError => Error is not null || string.IsNullOrEmpty(Code);
}

/// <summary>Texte der kleinen Seite, die der Browser nach dem Rücksprung zeigt (aus den App-Ressourcen, DE/EN/FR).</summary>
public sealed record LoopbackPageTexts(string Language, string Title, string Success, string Failure)
{
    public static LoopbackPageTexts GermanDefault { get; } = new(
        "de",
        "Rebels of Gaming",
        "Anmeldung abgeschlossen. Du kannst dieses Fenster schließen und zur App zurückkehren.",
        "Die Anmeldung wurde nicht abgeschlossen. Du kannst dieses Fenster schließen und es in der App erneut versuchen.");
}

/// <summary>
/// Minimaler HTTP-Empfänger für den OAuth-Rücksprung nativer Apps (RFC 8252 Abschnitt 7.3): lauscht nur auf
/// <c>127.0.0.1</c> mit einem zufälligen freien Port, nimmt genau eine Anfrage an <c>/callback</c> an und beendet sich.
/// <para>
/// Bewusst <see cref="TcpListener"/> statt <c>HttpListener</c>: <c>HttpListener</c> läuft unter Windows über http.sys
/// und braucht für Präfixe außer <c>localhost</c> je nach System eine URL-ACL (Admin-Rechte). Die Website erlaubt als
/// Loopback-Adresse aber nur IP-Literale, nicht <c>localhost</c>. Ein Socket auf 127.0.0.1 braucht keine Rechte und
/// verhält sich unter Windows und Linux (Tests) gleich.
/// </para>
/// </summary>
public sealed class LoopbackCallbackListener : IAsyncDisposable, IDisposable
{
    /// <summary>Pfad der Rücksprung-Adresse – muss in <c>APPS_LOOPBACK_PATHS</c> der Website stehen (Standard <c>/callback</c>).</summary>
    public const string CallbackPath = "/callback";

    private const int MaxRequestBytes = 16 * 1024;

    private readonly TcpListener _listener;
    private readonly LoopbackPageTexts _texts;
    private bool _disposed;

    private LoopbackCallbackListener(TcpListener listener, LoopbackPageTexts texts)
    {
        _listener = listener;
        _texts = texts;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        RedirectUri = new Uri($"http://127.0.0.1:{Port}{CallbackPath}");
    }

    public int Port { get; }

    /// <summary>Exakt diese Adresse geht als <c>redirect_uri</c> an Autorisierung und Code-Tausch.</summary>
    public Uri RedirectUri { get; }

    /// <summary>Startet den Empfänger auf 127.0.0.1 mit einem freien Port.</summary>
    public static LoopbackCallbackListener Start(LoopbackPageTexts? texts = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 4);
        return new LoopbackCallbackListener(listener, texts ?? LoopbackPageTexts.GermanDefault);
    }

    /// <summary>
    /// Wartet auf den Rücksprung. Anfragen an andere Pfade (z. B. <c>/favicon.ico</c>) bekommen 404 und werden
    /// ignoriert. Abbruch über <paramref name="cancellationToken"/> (Timeout setzt der Aufrufer).
    /// </summary>
    public async Task<LoopbackCallback> WaitForCallbackAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            using var stream = client.GetStream();
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(10));

            string? target;
            try
            {
                target = await ReadRequestTargetAsync(stream, readTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                continue; // langsamer oder leerer Client – nächste Verbindung
            }
            catch (IOException)
            {
                continue;
            }

            if (target is null || !TryParseCallback(target, out var callback))
            {
                await WriteResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8", "Not Found", cancellationToken).ConfigureAwait(false);
                continue;
            }

            var html = BuildPage(callback.IsError ? _texts.Failure : _texts.Success);
            await WriteResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", html, cancellationToken).ConfigureAwait(false);
            return callback;
        }
    }

    /// <summary>Wertet das Anfrageziel (<c>/callback?code=…&amp;state=…</c>) aus.</summary>
    internal static bool TryParseCallback(string target, out LoopbackCallback callback)
    {
        callback = new LoopbackCallback(null, null, null, null);
        var queryStart = target.IndexOf('?', StringComparison.Ordinal);
        var path = queryStart < 0 ? target : target[..queryStart];
        if (!string.Equals(path, CallbackPath, StringComparison.Ordinal))
        {
            return false;
        }

        var query = ParseQuery(queryStart < 0 ? string.Empty : target[(queryStart + 1)..]);
        query.TryGetValue("code", out var code);
        query.TryGetValue("state", out var state);
        query.TryGetValue("error", out var error);
        query.TryGetValue("error_description", out var description);
        callback = new LoopbackCallback(code, state, error, description);
        return true;
    }

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            var key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
            var value = eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            result.TryAdd(key, value); // erster Wert gewinnt, doppelte Parameter werden ignoriert
        }

        return result;
    }

    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxRequestBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            var lineEnd = text.IndexOf("\r\n", StringComparison.Ordinal);
            if (lineEnd >= 0)
            {
                // Nur die Anfragezeile zählt: "GET /callback?… HTTP/1.1"
                var parts = text[..lineEnd].Split(' ');
                return parts.Length == 3 && parts[0] == "GET" && parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal) ? parts[1] : null;
            }
        }

        return null;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string reason, string contentType, string body, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {bytes.Length}\r\n" +
                   "Cache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\n" +
                   "Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'\r\nConnection: close\r\n\r\n";
        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Browser hat die Verbindung schon geschlossen – unkritisch.
        }
    }

    private string BuildPage(string message)
    {
        var title = WebUtility.HtmlEncode(_texts.Title);
        var text = WebUtility.HtmlEncode(message);
        var lang = WebUtility.HtmlEncode(_texts.Language);
        // Farben aus den RoG-Tokens (bg, bg2, line, ink, red) – keine externen Ressourcen.
        return $$"""
            <!doctype html><html lang="{{lang}}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{title}}</title><style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#1C1F26;color:#F1EFEA;
            font-family:system-ui,"Segoe UI",sans-serif}main{max-width:28rem;margin:1rem;padding:2rem;background:#242832;border:1px solid #3A4050;
            border-top:3px solid #E0324A;border-radius:12px}h1{margin:0 0 .75rem;font-size:1.25rem;letter-spacing:.06em;text-transform:uppercase}
            p{margin:0;line-height:1.5;color:#A8ACB6}</style></head><body><main><h1>{{title}}</h1><p>{{text}}</p></main></body></html>
            """;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listener.Stop();
        _listener.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
