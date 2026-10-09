using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;

namespace Puls8.Net;

internal static class Http
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        // Timeouts are applied per request: a single client-wide limit would cut large pack downloads short on slow connections.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0";
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Puls8-Dalamud", version));
        return client;
    }
}
