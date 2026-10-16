namespace RoG.Core.Features.Servers;

/// <summary>Womit „Verbinden“ den Server öffnet.</summary>
public enum ConnectKind
{
    /// <summary>Kein Link – nur Adresse kopieren.</summary>
    None,

    /// <summary><c>steam://connect/host:port</c> – Steam startet das Spiel.</summary>
    Steam,

    /// <summary><c>et://host:port</c> bzw. <c>wolf://…</c> – ET: Legacy/RtCW registrieren das Schema.</summary>
    EnemyTerritory,

    /// <summary>Minecraft: eigener <c>minecraft://</c>-Link (Bedrock) oder ohne Link (Java: Adresse von Hand eintragen).</summary>
    Minecraft,

    /// <summary>http(s)-Link (z. B. Webseite des Servers) – öffnet der Browser.</summary>
    Web,
}

/// <summary>Was ein Klick auf „Verbinden“ tun soll: Link öffnen (falls vorhanden) und welche Adresse sonst kopiert wird.</summary>
public sealed record ConnectPlan(ConnectKind Kind, Uri? LaunchUri, string Address);

/// <summary>Ergebnis von „Verbinden“ – die Oberfläche zeigt dazu den passenden Hinweis.</summary>
public enum ConnectResult
{
    /// <summary>Link an das Spiel/Steam übergeben.</summary>
    Launched,

    /// <summary>Kein Programm für den Link (Steam/ET nicht installiert) – Adresse kopiert.</summary>
    CopiedNoHandler,

    /// <summary>Kein Link vorgesehen (Minecraft Java u. a.) – Adresse kopiert, im Spiel eintragen.</summary>
    CopiedManual,

    /// <summary>Weder Start noch Kopieren hat funktioniert.</summary>
    Failed,
}

public sealed record ConnectOutcome(ConnectResult Result, ConnectKind Kind, string Address, bool Passworded);

/// <summary>Startet Links über das Betriebssystem (App: <c>Windows.System.Launcher</c>).</summary>
public interface IExternalLauncher
{
    /// <summary>Ob ein installiertes Programm das Schema des Links behandelt (<c>QueryUriSupportAsync</c>).</summary>
    Task<bool> CanLaunchAsync(Uri uri);

    Task<bool> LaunchAsync(Uri uri);
}

/// <summary>Schreibt Text in die Zwischenablage.</summary>
public interface IClipboardService
{
    void SetText(string text);
}

/// <summary>
/// „Verbinden“ für Gameserver. Den Link liefert die API (<c>connect_url</c>: eigener Link oder abgeleitet –
/// <c>steam://connect/…</c> für Source, <c>et://…</c> für Quake 3, für Minecraft keiner). Angenommen werden nur die
/// Schemata, die auch die Website beim Speichern erlaubt; alles andere wird nie gestartet. Fehlt das Programm,
/// wird die Adresse kopiert, damit man sie im Spiel eintragen kann.
/// </summary>
public sealed class ServerConnector(IExternalLauncher launcher, IClipboardService clipboard)
{
    private static readonly Dictionary<string, ConnectKind> s_schemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["steam"] = ConnectKind.Steam,
        ["et"] = ConnectKind.EnemyTerritory,
        ["wolf"] = ConnectKind.EnemyTerritory,
        ["minecraft"] = ConnectKind.Minecraft,
        ["http"] = ConnectKind.Web,
        ["https"] = ConnectKind.Web,
    };

    public static ConnectPlan Plan(GameServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (TryParseLink(server.ConnectUrl, out var uri, out var kind))
        {
            return new ConnectPlan(kind, uri, server.Address);
        }

        var fallback = server.QueryType == ServerQueryType.Minecraft ? ConnectKind.Minecraft : ConnectKind.None;
        return new ConnectPlan(fallback, null, server.Address);
    }

    public async Task<ConnectOutcome> ConnectAsync(GameServer server)
    {
        var plan = Plan(server);
        if (plan.LaunchUri is { } uri && (plan.Kind == ConnectKind.Web || await SafeAsync(() => launcher.CanLaunchAsync(uri)).ConfigureAwait(true)))
        {
            if (await SafeAsync(() => launcher.LaunchAsync(uri)).ConfigureAwait(true))
            {
                return new ConnectOutcome(ConnectResult.Launched, plan.Kind, plan.Address, server.Passworded);
            }
        }

        var result = plan.LaunchUri is null ? ConnectResult.CopiedManual : ConnectResult.CopiedNoHandler;
        return new ConnectOutcome(Copy(plan.Address) ? result : ConnectResult.Failed, plan.Kind, plan.Address, server.Passworded);
    }

    /// <summary>Kopiert die Adresse (<c>host:port</c>); <c>false</c>, wenn die Zwischenablage gesperrt ist.</summary>
    public bool Copy(string address)
    {
        try
        {
            clipboard.SetText(address);
            return true;
        }
#pragma warning disable CA1031 // Zwischenablage kann von anderen Programmen gesperrt sein – nur melden
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    internal static bool TryParseLink(string? value, out Uri? uri, out ConnectKind kind)
    {
        uri = null;
        kind = ConnectKind.None;
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl))
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || !s_schemes.TryGetValue(parsed.Scheme, out var k))
        {
            return false;
        }

        // Ohne Ziel nach „://“ startet z. B. steam:// nur den Client – das ist kein Verbinden.
        var rest = value[(value.IndexOf("://", StringComparison.Ordinal) + 3)..];
        if (rest.Length == 0)
        {
            return false;
        }

        uri = parsed;
        kind = k;
        return true;
    }

    private static async Task<bool> SafeAsync(Func<Task<bool>> action)
    {
        try
        {
            return await action().ConfigureAwait(true);
        }
#pragma warning disable CA1031 // Launcher-Fehler (Schema gesperrt, Richtlinie) → Adresse kopieren statt Absturz
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }
}

/// <summary>Ton einer Rückmeldung (App: InfoBar-Schwere).</summary>
public enum MessageTone
{
    Success,
    Info,
    Error,
}

/// <summary>Hinweis nach „Verbinden“: Text (übersetzt) und Ton.</summary>
public sealed record ConnectMessage(string Text, MessageTone Tone)
{
    /// <summary>Text je Ergebnis: gestartet, Programm fehlt (mit Konsolenbefehl je Spiel), Adresse kopiert, Fehler.</summary>
    public static ConnectMessage Describe(ConnectOutcome outcome, string serverName, Localization.ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(localizer);
        var key = outcome.Result switch
        {
            ConnectResult.Launched => outcome.Kind switch
            {
                ConnectKind.Steam => Localization.StringKeys.ServersLaunchedSteam,
                ConnectKind.Web => Localization.StringKeys.ServersLaunchedWeb,
                _ => Localization.StringKeys.ServersLaunchedGame,
            },
            ConnectResult.CopiedNoHandler => outcome.Kind switch
            {
                ConnectKind.Steam => Localization.StringKeys.ServersNoHandlerSteam,
                ConnectKind.EnemyTerritory => Localization.StringKeys.ServersNoHandlerEt,
                _ => Localization.StringKeys.ServersNoHandlerOther,
            },
            ConnectResult.CopiedManual => outcome.Kind == ConnectKind.Minecraft ? Localization.StringKeys.ServersManualMinecraft : Localization.StringKeys.ServersCopied,
            _ => Localization.StringKeys.ServersCopyFailed,
        };

        var text = string.Format(System.Globalization.CultureInfo.CurrentCulture, localizer.GetString(key),
            outcome.Result == ConnectResult.Launched ? serverName : outcome.Address);
        if (outcome.Passworded && outcome.Result != ConnectResult.Failed)
        {
            text += " " + localizer.GetString(Localization.StringKeys.ServersPassworded);
        }

        return new ConnectMessage(text, outcome.Result switch
        {
            ConnectResult.Launched => MessageTone.Success,
            ConnectResult.Failed => MessageTone.Error,
            _ => MessageTone.Info,
        });
    }
}
