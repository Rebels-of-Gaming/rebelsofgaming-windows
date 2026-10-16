using System.Text.Json;
using RoG.Core.Api;
using RoG.Core.Api.Generated;

namespace RoG.Core.Features.Voice;

public sealed record TsClient(string Nickname, bool Away, bool Muted, string? MemberName);

public sealed record TsChannel(string Name, bool Spacer, IReadOnlyList<TsClient> Clients, IReadOnlyList<TsChannel> Children)
{
    /// <summary>Kanäle samt Unterkanälen mit mindestens einer Person (für die kompakte Ansicht).</summary>
    public bool HasPeople => Clients.Count > 0 || Children.Any(c => c.HasPeople);
}

public sealed record TeamSpeakState(bool Online, string ServerName, int ClientsOnline, int MaxClients, string? Address, IReadOnlyList<TsChannel> Channels);

public sealed record DiscordVoiceChannel(string Name, bool Private, IReadOnlyList<(string Name, Uri? Avatar)> Members);

/// <summary>TeamSpeak-Viewer und Discord-Voice (wer ist wo) samt „Verbinden“-Link für TeamSpeak.</summary>
public sealed class VoiceService(IRoGApiClient client)
{
    public async Task<TeamSpeakState> GetTeamSpeakAsync(CancellationToken cancellationToken = default)
    {
        var d = (await client.V1_teamspeak_viewerAsync(cancellationToken).ConfigureAwait(false)).Data;
        var channels = d.Channels.Select(c => new TsChannel(
            c.Name,
            c.Spacer,
            c.Clients.Select(x => new TsClient(x.Nickname, x.Away, x.Muted, x.Member?.Name)).ToList(),
            c.Children.Select(ParseChild).OfType<TsChannel>().ToList())).ToList();
        return new TeamSpeakState(d.Online, d.Server.Name, d.Server.Clients_online, d.Server.Max_clients, ApiValues.Text(d.Server.Address), channels);
    }

    public async Task<IReadOnlyList<DiscordVoiceChannel>> GetDiscordAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.V1_discord_voiceAsync(cancellationToken).ConfigureAwait(false);
        return response.Data
            .Select(c => new DiscordVoiceChannel(c.Channel, c.Private, c.Members.Select(m => (m.Name, ApiValues.WebUri(m.Avatar))).ToList()))
            .ToList();
    }

    /// <summary>
    /// <c>ts3server://host?port=…&amp;nickname=…</c> aus der Adresse (<c>host[:port]</c>); <c>null</c> ohne gültigen Host.
    /// Der Spitzname ist der RoG-Name, damit das Mitglied im TeamSpeak erkannt wird.
    /// </summary>
    public static Uri? TeamSpeakLink(string? address, string? nickname)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var value = address.Trim();
        string host = value;
        string? port = null;
        var colon = value.LastIndexOf(':');
        if (colon > 0 && !value.EndsWith(']') && int.TryParse(value[(colon + 1)..], out var p) && p is > 0 and < 65536)
        {
            host = value[..colon];
            port = p.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (Uri.CheckHostName(host.Trim('[', ']')) == UriHostNameType.Unknown)
        {
            return null;
        }

        var query = new List<string>();
        if (port is not null)
        {
            query.Add("port=" + port);
        }

        if (!string.IsNullOrWhiteSpace(nickname))
        {
            query.Add("nickname=" + Uri.EscapeDataString(nickname.Trim()));
        }

        return new Uri("ts3server://" + host + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty));
    }

    /// <summary>Unterkanäle sind in der Spec untypisiert (rekursiv) – aus dem JSON lesen.</summary>
    internal static TsChannel? ParseChild(IDictionary<string, object> child)
    {
        var element = JsonSerializer.SerializeToElement(child);
        return ParseElement(element);
    }

    private static TsChannel? ParseElement(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : string.Empty;
        var spacer = e.TryGetProperty("spacer", out var s) && s.ValueKind == JsonValueKind.True;
        var clients = new List<TsClient>();
        if (e.TryGetProperty("clients", out var cs) && cs.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cs.EnumerateArray())
            {
                var nick = c.TryGetProperty("nickname", out var nn) && nn.ValueKind == JsonValueKind.String ? nn.GetString()! : "?";
                var away = c.TryGetProperty("away", out var a) && a.ValueKind == JsonValueKind.True;
                var muted = c.TryGetProperty("muted", out var m) && m.ValueKind == JsonValueKind.True;
                var member = c.TryGetProperty("member", out var mem) && mem.ValueKind == JsonValueKind.Object && mem.TryGetProperty("name", out var mn) ? mn.GetString() : null;
                clients.Add(new TsClient(nick, away, muted, member));
            }
        }

        var children = new List<TsChannel>();
        if (e.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array)
        {
            children.AddRange(ch.EnumerateArray().Select(ParseElement).OfType<TsChannel>());
        }

        return new TsChannel(name, spacer, clients, children);
    }
}
