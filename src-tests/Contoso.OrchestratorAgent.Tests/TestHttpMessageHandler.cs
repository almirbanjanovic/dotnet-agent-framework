using System.Net;

namespace Contoso.OrchestratorAgent.Tests;

internal sealed class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    public HttpRequestMessage? Request { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Request = request;
        return await _handler(request, cancellationToken);
    }

    public static TestHttpMessageHandler Create(string payload, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new TestHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(payload)
        }));
    }
}
