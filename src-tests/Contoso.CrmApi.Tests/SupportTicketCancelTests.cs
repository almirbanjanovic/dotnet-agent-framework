using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Contoso.CrmApi.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace Contoso.CrmApi.Tests;

// Behaviour tests for the new ticket-cancellation flow and the
// fire-and-forget refund-alert side-effect on POST /tickets.
public class SupportTicketCancelTests : IClassFixture<CrmApiWebApplicationFactory>
{
    private readonly CrmApiWebApplicationFactory _factory;

    public SupportTicketCancelTests(CrmApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ---------- PATCH /tickets/{id} ----------

    [Fact]
    public async Task PatchTicket_HappyPath_CancelsOpenTicket()
    {
        // Use a freshly created ticket so we don't mutate seeded fixtures
        // that other tests in the class fixture rely on.
        var client = _factory.CreateClient();
        var created = await CreateOpenTicketAsync(client, customerId: "101");

        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tickets/{created!.Id}")
        {
            Content = JsonContent.Create(new { status = "cancelled", customer_id = "101" })
        };
        patch.Headers.Add("X-Customer-Entra-Id", "101");

        var response = await client.SendAsync(patch);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<SupportTicket>();
        updated!.Status.Should().Be("cancelled");
        updated.ClosedAt.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PatchTicket_DifferentCustomer_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        var created = await CreateOpenTicketAsync(client, customerId: "101");

        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tickets/{created!.Id}")
        {
            Content = JsonContent.Create(new { status = "cancelled", customer_id = "102" })
        };
        patch.Headers.Add("X-Customer-Entra-Id", "102");

        var response = await client.SendAsync(patch);

        // 404 (not 403) so an attacker cannot probe which ticket ids exist.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchTicket_AlreadyClosed_ReturnsConflict()
    {
        var client = _factory.CreateClient();

        // ST-005 is the only seeded ticket with status="closed" — perfect
        // for the not-open guard. Customer is 110.
        var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/tickets/ST-005")
        {
            Content = JsonContent.Create(new { status = "cancelled", customer_id = "110" })
        };
        patch.Headers.Add("X-Customer-Entra-Id", "110");

        var response = await client.SendAsync(patch);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PatchTicket_InvalidStatus_ReturnsValidationProblem()
    {
        var client = _factory.CreateClient();
        var created = await CreateOpenTicketAsync(client, customerId: "101");

        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tickets/{created!.Id}")
        {
            Content = JsonContent.Create(new { status = "weaponised", customer_id = "101" })
        };
        patch.Headers.Add("X-Customer-Entra-Id", "101");

        var response = await client.SendAsync(patch);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("status");
    }

    [Fact]
    public async Task PatchTicket_Unknown_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/tickets/ST-DOES-NOT-EXIST")
        {
            Content = JsonContent.Create(new { status = "cancelled", customer_id = "101" })
        };
        patch.Headers.Add("X-Customer-Entra-Id", "101");

        var response = await client.SendAsync(patch);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchTicket_NoHeader_BodyCustomerIdIsIgnored_Returns401()
    {
        // Adversarial-review fix A1/B1: the body customer_id is no
        // longer trusted as a fallback. A caller without the
        // X-Customer-Entra-Id header gets 401 instead of being able
        // to mutate someone else's ticket via the body.
        var client = _factory.CreateClient();
        var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/tickets/ST-001")
        {
            Content = JsonContent.Create(new { status = "cancelled", customer_id = "104" })
        };
        // NB: deliberately NOT adding the X-Customer-Entra-Id header.

        var response = await client.SendAsync(patch);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------- POST /tickets — refund-alert side-effect ----------

    [Fact]
    public async Task CreateTicket_ReturnCategoryWithOrder_TriggersFraudWorkflowAlert()
    {
        // Replace the per-test factory's stub so we can assert on the
        // outbound POST /api/v1/refunds. Each test gets its own factory
        // when we want clean handler state.
        await using var factory = new CrmApiWebApplicationFactory();
        var captured = new System.Collections.Concurrent.ConcurrentQueue<string>();
        factory.FraudWorkflowHandler = new StubHttpMessageHandler(req =>
        {
            // Read the body off the synchronous handler thread — by the
            // time the assertion runs the request has been disposed.
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            captured.Enqueue($"{req.Method} {req.RequestUri?.PathAndQuery} :: {body}");
            return StubHttpMessageHandler.Accepted("{\"alertId\":\"alert-test\"}");
        });

        var client = factory.CreateClient();

        // Customer 103 / Order 1003 exists in seed data (total_amount 349.99).
        var request = new CreateTicketRequest
        {
            CustomerId = "103",
            OrderId = "1003",
            Category = "return",
            Priority = "medium",
            Subject = "Refund please",
            Description = "Tent rainfly arrived torn."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // The fraud-workflow call is detached (Task.Run-equivalent). Poll
        // briefly for it to land — we cap at ~3s so a real regression
        // doesn't hang the suite.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && captured.IsEmpty)
        {
            await Task.Delay(50);
        }

        captured.Should().NotBeEmpty("create_support_ticket(category=return) MUST trigger the refund-risk alert");
        var entry = captured.First();
        entry.Should().Contain("POST /api/v1/refunds");
        entry.Should().Contain("\"customerId\":\"103\"");
        entry.Should().Contain("\"orderId\":\"1003\"");
        entry.Should().Contain("349.99"); // amount comes from order, not the ticket
    }

    [Fact]
    public async Task CreateTicket_GeneralCategory_DoesNotTriggerFraudWorkflow()
    {
        await using var factory = new CrmApiWebApplicationFactory();
        var hits = 0;
        factory.FraudWorkflowHandler = new StubHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref hits);
            return StubHttpMessageHandler.Accepted("{}");
        });

        var client = factory.CreateClient();
        var request = new CreateTicketRequest
        {
            CustomerId = "101",
            OrderId = "1001",
            Category = "general", // NOT a refund
            Priority = "low",
            Subject = "Care advice",
            Description = "How do I waterproof my boots?"
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Give the (would-be) background task a chance to run. If it
        // misfires we want to catch it.
        await Task.Delay(500);
        hits.Should().Be(0, "non-return tickets must NOT pull operations away from their lunch");
    }

    [Fact]
    public async Task CreateTicket_ReturnCategoryWithoutOrder_DoesNotTriggerFraudWorkflow()
    {
        await using var factory = new CrmApiWebApplicationFactory();
        var hits = 0;
        factory.FraudWorkflowHandler = new StubHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref hits);
            return StubHttpMessageHandler.Accepted("{}");
        });

        var client = factory.CreateClient();
        var request = new CreateTicketRequest
        {
            CustomerId = "101",
            OrderId = null, // explicitly no order
            Category = "return",
            Priority = "low",
            Subject = "General refund question",
            Description = "What's your return policy?"
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await Task.Delay(500);
        hits.Should().Be(0, "no order id == no refund alert");
    }

    [Fact]
    public async Task CreateTicket_ReturnAgainstUnknownOrder_Returns404AndDoesNotTrigger()
    {
        // Adversarial-review fix A2: the order-ownership guard rejects
        // the ticket creation entirely. The fraud-workflow trigger is
        // unreachable because we 404 before persistence.
        await using var factory = new CrmApiWebApplicationFactory();
        var hits = 0;
        factory.FraudWorkflowHandler = new StubHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref hits);
            return StubHttpMessageHandler.Accepted("{}");
        });

        var client = factory.CreateClient();
        var request = new CreateTicketRequest
        {
            CustomerId = "101",
            OrderId = "9999999", // not in seed data
            Category = "return",
            Priority = "low",
            Subject = "Refund this order",
            Description = "I'd like a refund."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await Task.Delay(500);
        hits.Should().Be(0, "no order means no ticket means no alert");
    }

    [Fact]
    public async Task CreateTicket_ReturnAgainstSomeoneElsesOrder_Returns404AndDoesNotTrigger()
    {
        // Adversarial-review fix A2: a customer cannot file a return
        // against another customer's order. The CRM API verifies that
        // order.customer_id matches the resolved customer before
        // persisting the ticket OR firing the alert.
        await using var factory = new CrmApiWebApplicationFactory();
        var hits = 0;
        factory.FraudWorkflowHandler = new StubHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref hits);
            return StubHttpMessageHandler.Accepted("{}");
        });

        var client = factory.CreateClient();
        var request = new CreateTicketRequest
        {
            CustomerId = "101",       // attacker
            OrderId = "1003",         // belongs to customer 103 in seed data
            Category = "return",
            Priority = "high",
            Subject = "Refund please",
            Description = "I'd like to refund this order."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);

        // Same response as "order doesn't exist" — we don't reveal
        // whether an order id is real-but-owned-by-someone-else.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await Task.Delay(500);
        hits.Should().Be(0, "cross-customer order ids must never trigger an alert");
    }

    [Fact]
    public async Task CreateTicket_ReturnWithFraudWorkflowDown_StillReturnsCreated()
    {
        // The fire-and-forget client must swallow upstream failures. If
        // it didn't, an outage of fraud-workflow would block customer
        // ticket creation — which is the exact opposite of what the
        // architecture is supposed to deliver.
        await using var factory = new CrmApiWebApplicationFactory();
        factory.FraudWorkflowHandler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var client = factory.CreateClient();
        var request = new CreateTicketRequest
        {
            CustomerId = "103",
            OrderId = "1003",
            Category = "return",
            Priority = "medium",
            Subject = "Refund please",
            Description = "Defective."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---------- helpers ----------

    private static async Task<SupportTicket?> CreateOpenTicketAsync(HttpClient client, string customerId)
    {
        // Use 'general' so this helper doesn't accidentally trigger the
        // fraud-workflow side-effect we test elsewhere.
        var request = new CreateTicketRequest
        {
            CustomerId = customerId,
            OrderId = null,
            Category = "general",
            Priority = "low",
            Subject = "Test ticket",
            Description = "Created by SupportTicketCancelTests."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<SupportTicket>();
    }
}
