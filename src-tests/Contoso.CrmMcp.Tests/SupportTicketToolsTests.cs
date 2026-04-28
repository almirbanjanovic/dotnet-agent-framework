using System.Net;
using Contoso.CrmMcp.Clients;
using Contoso.CrmMcp.Models;
using Contoso.CrmMcp.Tools;
using FluentAssertions;
using ModelContextProtocol;

namespace Contoso.CrmMcp.Tests;

public sealed class SupportTicketToolsTests
{
    [Fact]
    public async Task GetSupportTicketsAsync_ClientThrows_WrapsInMcpException()
    {
        var (tools, _) = CreateTools("fail", HttpStatusCode.InternalServerError);

        var act = () => tools.GetSupportTicketsAsync("C-9", true);

        await act.Should().ThrowAsync<McpException>()
            .WithMessage("Failed to get support tickets for 'C-9'.*");
    }

    [Fact]
    public async Task CreateSupportTicketAsync_ClientThrows_WrapsInMcpException()
    {
        var (tools, _) = CreateTools("fail", HttpStatusCode.InternalServerError);
        var request = new CreateTicketRequest
        {
            CustomerId = "C-9",
            Category = "returns",
            Priority = "high",
            Subject = "Need help",
            Description = "Details"
        };

        var act = () => tools.CreateSupportTicketAsync(request);

        await act.Should().ThrowAsync<McpException>()
            .WithMessage("Failed to create support ticket.*");
    }

    private static (SupportTicketTools Tools, TestHttpMessageHandler Handler) CreateTools(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = TestHttpMessageHandler.CreateJson(json, statusCode);
        var client = new CrmApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        return (new SupportTicketTools(client), handler);
    }
}
