using System.Net;

namespace Contoso.BlazorUi.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    public List<string?> RequestBodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (request.Content is null)
        {
            RequestBodies.Add(null);
        }
        else
        {
            RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        }

        return _handler(request);
    }

    internal static HttpResponseMessage OkJson(string payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
}
