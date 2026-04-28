using System.Net;
using FluentAssertions;

namespace Contoso.BffApi.Tests;

public class CorsTests
{
    [Fact]
    public async Task CorsPolicy_BlazorOrigin_AllowsRequest()
    {
        using var factory = new BffApiWebApplicationFactory();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "http://localhost:5008");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values).Should().BeTrue();
        values!.Should().ContainSingle("http://localhost:5008");
    }
}
