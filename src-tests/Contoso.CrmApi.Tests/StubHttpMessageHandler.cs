using System.Collections.Concurrent;
using System.Net;

namespace Contoso.CrmApi.Tests;

// Stub HttpMessageHandler used by FraudWorkflowClient tests to avoid
// a real network call to localhost:5010. Inlined (instead of shared)
// because the architecture HARD RULE forbids cross-project references
// between src-tests projects.
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    public int RequestCount => Requests.Count;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the body now — the response handler may inspect it
        // and the underlying stream is read-once after dispatch.
        Requests.Enqueue(request);
        return Task.FromResult(_handler(request));
    }

    internal static HttpResponseMessage Accepted(string body) =>
        new(HttpStatusCode.Accepted)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
}
