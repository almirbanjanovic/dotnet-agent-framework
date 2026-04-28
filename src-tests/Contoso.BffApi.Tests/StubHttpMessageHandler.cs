using System.Net;

namespace Contoso.BffApi.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_handler(request));
    }

    internal static HttpResponseMessage OkJson(string payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
}
