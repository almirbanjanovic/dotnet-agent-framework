using Microsoft.AspNetCore.Http;

namespace Contoso.OrchestratorAgent.Services;

/// <summary>
/// Forwards the <c>X-Customer-Entra-Id</c> header from the inbound chat
/// request to outbound specialist-agent calls so the customer identity
/// flows end-to-end through the orchestration chain.
/// </summary>
internal sealed class CustomerHeaderForwarder : DelegatingHandler
{
    public const string HeaderName = "X-Customer-Entra-Id";

    private readonly IHttpContextAccessor _accessor;

    public CustomerHeaderForwarder(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var inbound = _accessor.HttpContext?.Request.Headers[HeaderName].ToString();
        if (!string.IsNullOrWhiteSpace(inbound) && !request.Headers.Contains(HeaderName))
        {
            request.Headers.Add(HeaderName, inbound);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
