using System.Security.Claims;
using Contoso.BffApi.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace Contoso.BffApi.Tests;

public class CustomerContextTests
{
    [Fact]
    public void GetCustomerId_InMemoryMode_ReadsHeader()
    {
        var accessor = BuildAccessor(headers: new Dictionary<string, string> { ["X-Customer-Id"] = "cust-1" });
        var context = new CustomerContext(accessor, useHeader: true);

        var result = context.GetCustomerId();

        result.Should().Be("cust-1");
    }

    [Fact]
    public void GetCustomerId_InMemoryMode_MissingHeader_ReturnsNull()
    {
        var accessor = BuildAccessor();
        var context = new CustomerContext(accessor, useHeader: true);

        var result = context.GetCustomerId();

        result.Should().BeNull();
    }

    [Fact]
    public void GetCustomerId_JwtMode_ReadsCustomerIdClaim()
    {
        var accessor = BuildAccessor(claims: new[] { new Claim("customer_id", "cust-1") });
        var context = new CustomerContext(accessor, useHeader: false);

        var result = context.GetCustomerId();

        result.Should().Be("cust-1");
    }

    [Fact]
    public void GetCustomerId_JwtMode_FallsBackToNameIdentifier()
    {
        var accessor = BuildAccessor(claims: new[] { new Claim(ClaimTypes.NameIdentifier, "cust-2") });
        var context = new CustomerContext(accessor, useHeader: false);

        var result = context.GetCustomerId();

        result.Should().Be("cust-2");
    }

    [Fact]
    public void GetCustomerId_JwtMode_FallsBackToOid()
    {
        var accessor = BuildAccessor(claims: new[] { new Claim("oid", "cust-3") });
        var context = new CustomerContext(accessor, useHeader: false);

        var result = context.GetCustomerId();

        result.Should().Be("cust-3");
    }

    [Fact]
    public void GetCustomerId_JwtMode_FallsBackToSub()
    {
        var accessor = BuildAccessor(claims: new[] { new Claim("sub", "cust-4") });
        var context = new CustomerContext(accessor, useHeader: false);

        var result = context.GetCustomerId();

        result.Should().Be("cust-4");
    }

    private static IHttpContextAccessor BuildAccessor(
        IDictionary<string, string>? headers = null,
        IEnumerable<Claim>? claims = null)
    {
        var httpContext = new DefaultHttpContext();
        if (headers is not null)
        {
            foreach (var header in headers)
            {
                httpContext.Request.Headers[header.Key] = header.Value;
            }
        }

        if (claims is not null)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt"));
        }

        return new HttpContextAccessor { HttpContext = httpContext };
    }
}
