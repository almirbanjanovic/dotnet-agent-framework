using System.Net;

namespace Contoso.CrmMcp.Tests;

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
        var response = await _handler(request, cancellationToken);
        response.RequestMessage = request;
        return response;
    }

    public static TestHttpMessageHandler CreateJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new TestHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json)
        }));
    }
}
