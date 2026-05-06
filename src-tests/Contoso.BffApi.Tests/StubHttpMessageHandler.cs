using System.Collections.Concurrent;
using System.Net;

namespace Contoso.BffApi.Tests;

public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    // ConcurrentQueue rather than List<T> — the BFF orders endpoint now
    // fans out via Parallel.ForEachAsync, so SendAsync can be called from
    // multiple threads against this same handler.
    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        return Task.FromResult(_handler(request));
    }

    internal static HttpResponseMessage OkJson(string payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
}
