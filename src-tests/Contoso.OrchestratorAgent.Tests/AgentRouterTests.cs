using System.Net;
using Contoso.OrchestratorAgent.Models;
using Contoso.OrchestratorAgent.Services;
using FluentAssertions;

namespace Contoso.OrchestratorAgent.Tests;

public sealed class AgentRouterTests
{
    [Fact]
    public async Task RouteAsync_CrmIntent_UsesCrmAgentClient()
    {
        var crmHandler = TestHttpMessageHandler.Create("crm");
        var productHandler = TestHttpMessageHandler.Create("product");
        var router = CreateRouter(crmHandler, productHandler);

        _ = await router.RouteAsync("CRM", new ChatRequest("C-1", "hi"), CancellationToken.None);

        crmHandler.Request.Should().NotBeNull();
        crmHandler.Request!.RequestUri!.ToString().Should().Be("http://crm/api/v1/chat");
        productHandler.Request.Should().BeNull();
    }

    [Fact]
    public async Task RouteAsync_ProductIntent_UsesProductAgentClient()
    {
        var crmHandler = TestHttpMessageHandler.Create("crm");
        var productHandler = TestHttpMessageHandler.Create("product");
        var router = CreateRouter(crmHandler, productHandler);

        _ = await router.RouteAsync("PRODUCT", new ChatRequest("C-1", "hi"), CancellationToken.None);

        productHandler.Request.Should().NotBeNull();
        productHandler.Request!.RequestUri!.ToString().Should().Be("http://product/api/v1/chat");
        crmHandler.Request.Should().BeNull();
    }

    [Fact]
    public async Task RouteAsync_UnknownIntent_DefaultsToCrm()
    {
        var crmHandler = TestHttpMessageHandler.Create("crm");
        var productHandler = TestHttpMessageHandler.Create("product");
        var router = CreateRouter(crmHandler, productHandler);

        _ = await router.RouteAsync("UNKNOWN", new ChatRequest("C-1", "hi"), CancellationToken.None);

        crmHandler.Request.Should().NotBeNull();
        productHandler.Request.Should().BeNull();
    }

    [Fact]
    public async Task RouteAsync_AgentDown_Throws()
    {
        var crmHandler = new TestHttpMessageHandler((_, _) => throw new HttpRequestException("down"));
        var productHandler = TestHttpMessageHandler.Create("product");
        var router = CreateRouter(crmHandler, productHandler);

        var act = () => router.RouteAsync("CRM", new ChatRequest("C-1", "hi"), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("down");
    }

    [Fact]
    public async Task RouteAsync_AgentReturns500_PassesThroughPayload()
    {
        var crmHandler = TestHttpMessageHandler.Create("{\"error\":\"fail\"}", HttpStatusCode.InternalServerError);
        var productHandler = TestHttpMessageHandler.Create("product");
        var router = CreateRouter(crmHandler, productHandler);

        var result = await router.RouteAsync("CRM", new ChatRequest("C-1", "hi"), CancellationToken.None);

        result.StatusCode.Should().Be(500);
        result.Payload.Should().Be("{\"error\":\"fail\"}");
    }

    private static AgentRouter CreateRouter(TestHttpMessageHandler crmHandler, TestHttpMessageHandler productHandler)
    {
        var crmClient = new HttpClient(crmHandler) { BaseAddress = new Uri("http://crm") };
        var productClient = new HttpClient(productHandler) { BaseAddress = new Uri("http://product") };

        return new AgentRouter(new CrmAgentClient(crmClient), new ProductAgentClient(productClient));
    }
}
