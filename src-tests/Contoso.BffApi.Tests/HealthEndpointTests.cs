using System.Net;
using System.Text;
using FluentAssertions;

namespace Contoso.BffApi.Tests;

public class HealthEndpointTests
{
    [Fact]
    public async Task HealthEndpoint_Returns200()
    {
        using var factory = new BffApiWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadyEndpoint_InMemoryMode_Returns200()
    {
        using var factory = new BffApiWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadyEndpoint_OrchestratorDown_Returns503()
    {
        using var factory = new BffApiWebApplicationFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("down", Encoding.UTF8, "text/plain")
            });
        var client = factory.CreateClient();

        var response = await client.GetAsync("/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
