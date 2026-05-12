using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    [Fact]
    public async Task CancelSupportTicketAsync_HappyPath_PatchesCrmApiWithCancelledStatus()
    {
        // Stub returns a SupportTicket payload mirroring what CRM API
        // would return after the PATCH lands.
        var ticketJson = """
            {
                "id": "ST-001",
                "customer_id": "104",
                "order_id": "1004",
                "category": "return",
                "subject": "Damaged jacket received",
                "description": "Tear in sleeve.",
                "status": "cancelled",
                "priority": "high",
                "opened_at": "2026-02-26",
                "closed_at": "2026-03-10"
            }
            """;

        // Capture method/url/body INSIDE the handler — the HttpClient
        // disposes the request content right after SendAsync returns,
        // so we can't read it afterwards from handler.Request.
        HttpMethod? capturedMethod = null;
        string? capturedPath = null;
        string? capturedBody = null;
        var handler = new TestHttpMessageHandler(async (req, _) =>
        {
            capturedMethod = req.Method;
            capturedPath = req.RequestUri?.PathAndQuery;
            capturedBody = req.Content is not null ? await req.Content.ReadAsStringAsync() : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ticketJson)
            };
        });
        var crmClient = new CrmApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var tools = new SupportTicketTools(crmClient);

        var result = await tools.CancelSupportTicketAsync("ST-001", "104");

        capturedMethod.Should().Be(HttpMethod.Patch);
        capturedPath.Should().Be("/api/v1/tickets/ST-001");

        using var body = JsonDocument.Parse(capturedBody!);
        body.RootElement.GetProperty("status").GetString().Should().Be("cancelled");
        body.RootElement.GetProperty("customer_id").GetString().Should().Be("104");

        result.Should().Contain("\"status\":\"cancelled\"");
        result.Should().Contain("ST-001");
    }

    [Fact]
    public async Task CancelSupportTicketAsync_CrmReturnsConflict_WrapsInMcpException()
    {
        var (tools, _) = CreateTools("{\"detail\":\"already closed\"}", HttpStatusCode.Conflict);

        var act = () => tools.CancelSupportTicketAsync("ST-005", "110");

        await act.Should().ThrowAsync<McpException>()
            .WithMessage("Failed to cancel support ticket 'ST-005'.*");
    }

    [Fact]
    public async Task CancelSupportTicketAsync_CrmReturnsNotFound_WrapsInMcpException()
    {
        var (tools, _) = CreateTools("{}", HttpStatusCode.NotFound);

        var act = () => tools.CancelSupportTicketAsync("ST-DOES-NOT-EXIST", "101");

        await act.Should().ThrowAsync<McpException>()
            .WithMessage("Failed to cancel support ticket 'ST-DOES-NOT-EXIST'.*");
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
