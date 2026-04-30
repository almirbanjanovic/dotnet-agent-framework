using Microsoft.AspNetCore.Http;

namespace Contoso.BffApi.Services;

/// <summary>
/// Outbound message handler that injects the customer identity into every
/// downstream HTTP call as <c>X-Customer-Entra-Id</c>. This is the only way
/// internal services know which user a request is for.
/// </summary>
public sealed class CustomerHeaderHandler : DelegatingHandler
{
    public const string HeaderName = "X-Customer-Entra-Id";

    private readonly CustomerContext _context;

    public CustomerHeaderHandler(CustomerContext context)
    {
        _context = context;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var customerId = _context.GetCustomerId();
        if (!string.IsNullOrWhiteSpace(customerId) && !request.Headers.Contains(HeaderName))
        {
            request.Headers.Add(HeaderName, customerId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
