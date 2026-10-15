using RoG.Core.Api.Generated;
using RoG.Core.Auth;
using RoG.Core.Config;

namespace RoG.Core.Api;

/// <summary>
/// Baut den <see cref="HttpClient"/> mit der RoG-Pipeline und den generierten <see cref="RoGApiClient"/>.
/// Eine Instanz je App-Sitzung; die Umgebung (Produktion/Staging/lokal) kommt aus den Einstellungen.
/// </summary>
public sealed class RoGApi : IDisposable
{
    public RoGApi(RoGEnvironment environment, ITokenStore tokens, IApiContext context, IEtagCache? etags = null, HttpMessageHandler? innerHandler = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        Environment = environment;
        Handler = new RoGHttpHandler(tokens, context, etags ?? new InMemoryEtagCache())
        {
            InnerHandler = innerHandler ?? new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            },
        };
        Http = new HttpClient(Handler, disposeHandler: true)
        {
            BaseAddress = environment.ApiBaseUrl,
            Timeout = TimeSpan.FromSeconds(30),
        };
        Client = new RoGApiClient(Http);
    }

    public RoGEnvironment Environment { get; }

    public RoGHttpHandler Handler { get; }

    /// <summary>Client mit Pipeline und BaseAddress <c>…/v1/</c> (relative Pfade ohne führenden Schrägstrich).</summary>
    public HttpClient Http { get; }

    /// <summary>Aus der OpenAPI-Spec generierter Client (<c>scripts/generate-api.sh</c>).</summary>
    public IRoGApiClient Client { get; }

    public void Dispose() => Http.Dispose();
}
