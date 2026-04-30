using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Contoso.BffApi.Services;

public sealed class CustomerContext
{
    private readonly IHttpContextAccessor _accessor;
    private readonly bool _useHeader;
    private readonly IReadOnlyDictionary<string, string> _customerMap;

    public CustomerContext(IHttpContextAccessor accessor, bool useHeader)
        : this(accessor, useHeader, customerMap: null)
    {
    }

    public CustomerContext(
        IHttpContextAccessor accessor,
        bool useHeader,
        IReadOnlyDictionary<string, string>? customerMap)
    {
        _accessor = accessor;
        _useHeader = useHeader;
        _customerMap = customerMap is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(customerMap, StringComparer.OrdinalIgnoreCase);
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

        // Prefer an explicit customer_id claim if the IdP issues one.
        var explicitId = user.FindFirst("customer_id")?.Value;
        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            return explicitId;
        }

        // For real Entra ID sign-in, map a known UPN/email/oid to a seeded
        // customer. The map lets the Local + Entra workflow reuse the
        // same customer dataset (101..109) without changing the CSVs.
        if (_customerMap.Count > 0)
        {
            foreach (var key in EnumerateMapLookupKeys(user))
            {
                if (!string.IsNullOrWhiteSpace(key) && _customerMap.TryGetValue(key, out var mapped))
                {
                    return mapped;
                }
            }
        }

        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("oid")?.Value
            ?? user.FindFirst("sub")?.Value;
    }

    private static IEnumerable<string?> EnumerateMapLookupKeys(ClaimsPrincipal user)
    {
        yield return user.FindFirst("preferred_username")?.Value;
        yield return user.FindFirst(ClaimTypes.Upn)?.Value;
        yield return user.FindFirst(ClaimTypes.Email)?.Value;
        yield return user.FindFirst("email")?.Value;
        yield return user.FindFirst("oid")?.Value;
    }
}
