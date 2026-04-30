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

    [Fact]
    public void GetCustomerId_JwtMode_WithCustomerMap_MapsByPreferredUsername()
    {
        // Local Track + real Entra: Emma signs in as emma.wilson@tenant.com,
        // map resolves her to seeded customer 101 without any claim engineering.
        var accessor = BuildAccessor(claims: new[]
        {
            new Claim("preferred_username", "emma.wilson@contoso.com"),
            new Claim("oid", "00000000-0000-0000-0000-000000000001")
        });
        var map = new Dictionary<string, string>
        {
            ["emma.wilson@contoso.com"] = "101"
        };
        var context = new CustomerContext(accessor, useHeader: false, customerMap: map);

        var result = context.GetCustomerId();

        result.Should().Be("101");
    }

    [Fact]
    public void GetCustomerId_JwtMode_CustomerMap_IsCaseInsensitive()
    {
        var accessor = BuildAccessor(claims: new[]
        {
            new Claim("preferred_username", "Emma.Wilson@CONTOSO.COM")
        });
        var map = new Dictionary<string, string> { ["emma.wilson@contoso.com"] = "101" };
        var context = new CustomerContext(accessor, useHeader: false, customerMap: map);

        var result = context.GetCustomerId();

        result.Should().Be("101");
    }

    [Fact]
    public void GetCustomerId_JwtMode_NoMapMatch_FallsBackToOid()
    {
        var accessor = BuildAccessor(claims: new[]
        {
            new Claim("preferred_username", "unknown@contoso.com"),
            new Claim("oid", "oid-fallback")
        });
        var map = new Dictionary<string, string> { ["emma.wilson@contoso.com"] = "101" };
        var context = new CustomerContext(accessor, useHeader: false, customerMap: map);

        var result = context.GetCustomerId();

        result.Should().Be("oid-fallback");
    }

    [Fact]
    public void GetCustomerId_JwtMode_CustomerIdClaim_WinsOverMap()
    {
        // Explicit customer_id claim (issued by a custom IdP) should win
        // over UPN-based map lookup so production policies can override.
        var accessor = BuildAccessor(claims: new[]
        {
            new Claim("customer_id", "explicit-cust"),
            new Claim("preferred_username", "emma.wilson@contoso.com")
        });
        var map = new Dictionary<string, string> { ["emma.wilson@contoso.com"] = "101" };
        var context = new CustomerContext(accessor, useHeader: false, customerMap: map);

        var result = context.GetCustomerId();

        result.Should().Be("explicit-cust");
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
