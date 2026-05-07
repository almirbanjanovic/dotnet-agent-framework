using Microsoft.AspNetCore.Http;

/// <summary>
/// Forwards the <c>X-Customer-Entra-Id</c> header from the inbound chat
/// request to outbound MCP-server calls so the customer identity flows
/// end-to-end (BFF → Orchestrator → CRM Agent → CRM/Knowledge MCP →
/// CRM API). Without this, downstream defense-in-depth checks cannot
/// scope reads to the authenticated customer.
/// </summary>
/// <remarks>
/// Intentionally duplicated from the Orchestrator and Product Agent —
/// the component-independence rule forbids a shared project; identical
/// code is preferred over coupling.
/// </remarks>
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
