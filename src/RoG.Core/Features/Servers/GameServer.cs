namespace RoG.Core.Features.Servers;

/// <summary>Abfrage-Protokoll eines Gameservers (<c>query_type</c> der API).</summary>
public enum ServerQueryType
{
    Unknown,

    /// <summary>Source/Steam-Spiele (CS2, Garry's Mod …) – Verbinden über <c>steam://connect/…</c>.</summary>
    Source,

    /// <summary>Quake-3-Engine (Enemy Territory, RtCW) – Verbinden über <c>et://…</c>.</summary>
    Quake3,

    /// <summary>Minecraft – kein Verbinden-Link, Adresse kopieren.</summary>
    Minecraft,
}

/// <summary>Spieler auf einem Server; <see cref="MemberName"/> nur, wenn die API ihn einem Mitglied zuordnet.</summary>
public sealed record ServerPlayer(string Name, int? Score, int? Ping, string? MemberName);

/// <summary>
/// Gameserver mit Live-Stand aus <c>GET /v1/servers</c> – eigenes Modell statt der generierten Typen
/// (<c>ServerResource</c>, <c>Live</c>), damit die Oberfläche nicht an Namen aus der Spec hängt.
/// </summary>
public sealed record GameServer(
    string Id,
    string Slug,
    string Name,
    string? GameName,
    string? GameShort,
    string Address,
    ServerQueryType QueryType,
    string? ConnectUrl,
    bool? Online,
    string? Map,
    int Players,
    int MaxPlayers,
    bool Passworded,
    IReadOnlyList<ServerPlayer>? PlayerList,
    ServerAbilities? Control = null)
{
    public static ServerQueryType ParseQueryType(string? value) => value?.ToUpperInvariant() switch
    {
        "SOURCE" => ServerQueryType.Source,
        "QUAKE3" => ServerQueryType.Quake3,
        "MINECRAFT" => ServerQueryType.Minecraft,
        _ => ServerQueryType.Unknown,
    };
}

/// <summary>
/// Wirksame Steuerungsrechte an einem Server (<c>control</c> aus der API, sonst <c>null</c>). Der Token-Scope gilt
/// zusätzlich – das prüft der Server; die App blendet nur aus, was sicher nicht geht.
/// </summary>
public sealed record ServerAbilities(bool Power, bool Console, bool ConsoleCommand, bool CanCommand)
{
    public bool Any => Power || Console;
}
