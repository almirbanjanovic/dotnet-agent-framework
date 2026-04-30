using Microsoft.AspNetCore.Http;

namespace Contoso.CrmApi.Services;

/// <summary>
/// Reads the customer identity from the inbound <c>X-Customer-Entra-Id</c>
/// HTTP header. Set by the BFF API after JWT validation (or by the dev-mode
/// dropdown). Used to scope CRM data access to the authenticated customer.
/// </summary>
public sealed class CustomerContext
{
    public const string HeaderName = "X-Customer-Entra-Id";

    private readonly IHttpContextAccessor _accessor;

    public CustomerContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    /// <summary>
    /// Returns the Entra object ID (or local dev customer ID) of the current
    /// caller, or <c>null</c> if the header is absent.
    /// </summary>
    public string? GetCustomerEntraId()
    {
        var context = _accessor.HttpContext;
        if (context is null)
        {
            return null;
        }

        var value = context.Request.Headers[HeaderName].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
