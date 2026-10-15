using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RoG.Core.Realtime;

/// <summary>Antwort der Kanal-Autorisierung (<c>auth</c>, bei Presence-Kanälen zusätzlich <c>channel_data</c>).</summary>
public sealed record ChannelAuthorization(
    [property: JsonPropertyName("auth")] string Auth,
    [property: JsonPropertyName("channel_data")] string? ChannelData);

/// <summary>Autorisiert private und Presence-Kanäle.</summary>
public interface IChannelAuthorizer
{
    Task<ChannelAuthorization> AuthorizeAsync(string socketId, string channelName, CancellationToken cancellationToken);
}

/// <summary>
/// <c>POST /v1/broadcasting/auth</c> mit <c>socket_id</c> und <c>channel_name</c> als Formular. Der
/// <see cref="HttpClient"/> trägt die RoG-Pipeline, also den Geräte-Token als Bearer. Kein Zugriff → 403 als
/// <see cref="Api.ApiProblemException"/>.
/// </summary>
public sealed class HttpChannelAuthorizer(HttpClient http, Uri authEndpoint) : IChannelAuthorizer
{
    public async Task<ChannelAuthorization> AuthorizeAsync(string socketId, string channelName, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("socket_id", socketId),
            new KeyValuePair<string, string>("channel_name", channelName),
        ]);
        using var request = new HttpRequestMessage(HttpMethod.Post, authEndpoint) { Content = content };
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<ChannelAuthorization>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Leere Antwort von broadcasting/auth.");
    }
}
