using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Contoso.BffApi.Services;

public sealed class CustomerContext
{
    private readonly IHttpContextAccessor _accessor;
    private readonly bool _useHeader;

    public CustomerContext(IHttpContextAccessor accessor, bool useHeader)
    {
        _accessor = accessor;
        _useHeader = useHeader;
    }

    public string? GetCustomerId()
    {
        var context = _accessor.HttpContext;
        if (context is null)
        {
            return null;
        }

        if (_useHeader)
        {
            var header = context.Request.Headers["X-Customer-Id"].ToString();
            return string.IsNullOrWhiteSpace(header) ? null : header;
        }

        var user = context.User;
        return user.FindFirst("customer_id")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("oid")?.Value
            ?? user.FindFirst("sub")?.Value;
    }
}
