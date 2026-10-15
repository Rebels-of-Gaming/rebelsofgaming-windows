using System.Net;
using System.Text;

namespace RoG.Core.Tests.Support;

/// <summary>Aufgezeichnete Anfrage (Header und Body zum Zeitpunkt des Sendens).</summary>
internal sealed record RecordedRequest(HttpMethod Method, Uri? Uri, Dictionary<string, string> Headers, string? Body);

/// <summary>Ersetzt das Netzwerk: beantwortet Anfragen über eine Funktion und zeichnet sie auf.</summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        if (request.Content is not null)
        {
            foreach (var h in request.Content.Headers)
            {
                headers[h.Key] = string.Join(", ", h.Value);
            }
        }

        Requests.Add(new RecordedRequest(request.Method, request.RequestUri, headers, body));
        var response = respond(request, Requests.Count - 1);
        response.RequestMessage ??= request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json, string contentType = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, contentType) };
}
